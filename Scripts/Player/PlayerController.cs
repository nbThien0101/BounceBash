using BounceBash.Config;
using BounceBash.Core;
using BounceBash.Physics;
using Godot;

namespace BounceBash.Player;

/// <summary>
/// Core player controller implementing GDD v0.9.5 Section 1.2.1, 1.2.2, 1.2.3, 1.3.1, 1.5.2-1.5.4, 1.6.2, and Tests T01-T08:
/// - GroundedIdle / GroundedMove / Charging / Airborne / Landing state machine
/// - Cyclic (Ping-Pong Min <-> Max) charge-and-release jumping (GDD v0.9.5 Slide 9 & 20)
/// - Releasing at either rising or falling phase uses the exact charge ratio at that moment
/// - No air control or double jump while Airborne
/// - Visual facing decoupled from physics; in air only flips when |vx| > FacingVelocityEpsilon
/// - Identical hitbox (14x24) and foot baseline anchor for Male and Female Explorers
/// - Kinetic Nullifier (Buff) & Direction Lock (Debuff) support
/// - Supports both Human input and Bot AI Controller driving (GDD v0.9.5 Slide 22)
/// </summary>
public partial class PlayerController : CharacterBody2D
{
    public int SlotIndex { get; set; }
    public int PeerId { get; set; } = 1;
    public string PlayerTag { get; set; } = "P1";
    public Color TagColor { get; set; } = Colors.Red;
    public CharacterGender Gender { get; set; } = CharacterGender.Male;
    public bool IsLocalSecondaryKeyboard { get; set; }
    public bool IsBot { get; set; }

    public PlayerState CurrentState { get; private set; } = PlayerState.GroundedIdle;
    public int FacingDirection { get; private set; } = 1; // -1 = West/Left, +1 = East/Right
    public int IntendedJumpDirection { get; private set; } // -1 = Left, 0 = Straight, +1 = Right
    public float CurrentChargeTime { get; private set; }
    public bool IsChargeAscending { get; private set; } = true;
    public float ChargeRatio => Tuning != null && Tuning.MaxChargeTime > 0f
        ? Mathf.Clamp(CurrentChargeTime / Tuning.MaxChargeTime, 0f, 1f)
        : 0f;

    // Lives & Elimination (GDD 1.2.2: 3 lives, 3rd fall = eliminated)
    public int RemainingLives { get; set; } = 3;
    public bool IsEliminated { get; set; }
    public bool RequireJumpReleaseAfterRespawn { get; set; }

    // Main / Aux Respawn anchors (GDD 1.3.3)
    public Vector2 MainRespawnPoint { get; set; }
    public Vector2 AuxRespawnPoint { get; set; }
    public bool HasValidMainRespawn { get; set; }
    public bool HasValidAuxRespawn { get; set; }

    // GoalZone tracking (GDD 1.2.5)
    public float PersonalGoalTimer { get; set; } = 5.0f;
    public int GoalZoneEntriesCount { get; set; }
    public float BestAltitudeReached { get; set; }

    // Buff: Kinetic Nullifier (GDD 1.2.3)
    public float KineticNullifierRemaining { get; private set; }
    public bool HasKineticNullifier => KineticNullifierRemaining > 0f;

    // Debuff: Direction Lock (GDD 1.2.3 & T08 - persists across respawn!)
    public float DirectionLockRemaining { get; private set; }
    public bool HasDirectionLock => DirectionLockRemaining > 0f;
    public int LockedJumpDirection { get; private set; } // -1 = Left, 0 = Straight, +1 = Right

    // Network / Input snapshot state
    public float NetMoveInput { get; set; }
    public bool NetJumpHeld { get; set; }
    public bool NetJumpReleasedBuffer { get; set; }

    private TuningConfig Tuning => GameSession.Instance.Tuning;
    private float _landingTimer;
    private float _bounceSfxCooldown;
    private float _rawChargeElapsed;

    private Sprite2D _sprite = null!;
    private CollisionShape2D _collisionShape = null!;

    // Preloaded 32x32 textures for Male & Female Explorer (Idle, Move, Charge x South, East, West)
    private Texture2D? _texIdleSouth;
    private Texture2D? _texIdleEast;
    private Texture2D? _texIdleWest;
    private Texture2D? _texMoveEast;
    private Texture2D? _texMoveWest;
    private Texture2D? _texChargeSouth;
    private Texture2D? _texChargeEast;
    private Texture2D? _texChargeWest;

    public const float ColliderWidth = 14.0f;
    public const float ColliderHeight = 24.0f;

    public override void _Ready()
    {
        // Configure CharacterBody2D for platforming
        FloorStopOnSlope = true;
        FloorMaxAngle = Mathf.DegToRad(40f);
        UpDirection = Vector2.Up;

        // Standardized collider for both Male and Female Explorer (GDD Section 1.5.4 & T05)
        _collisionShape = new CollisionShape2D();
        var rect = new RectangleShape2D
        {
            Size = new Vector2(ColliderWidth, ColliderHeight)
        };
        _collisionShape.Shape = rect;
        // Center collider so bottom of collider sits at Y = 0 (foot anchor baseline!)
        _collisionShape.Position = new Vector2(0f, -ColliderHeight * 0.5f);
        AddChild(_collisionShape);

        // Sprite2D anchored so feet at row 30 of 32x32 sit at Y = 0
        _sprite = new Sprite2D
        {
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Centered = false,
            Offset = new Vector2(-16f, -30f)
        };
        AddChild(_sprite);

        LoadCharacterSprites();
        UpdateSpritePose();

        RemainingLives = Tuning.InitialLives;
        PersonalGoalTimer = Tuning.GoalHoldTimeSeconds;
        BestAltitudeReached = GlobalPosition.Y;
    }

    public void LoadCharacterSprites()
    {
        string prefix = Gender == CharacterGender.Female ? "female" : "male";
        _texIdleSouth = LoadSpriteOrNull($"res://Assets/Sprites/Characters/{prefix}_idle_south.png");
        _texIdleEast = LoadSpriteOrNull($"res://Assets/Sprites/Characters/{prefix}_idle_east.png");
        _texIdleWest = LoadSpriteOrNull($"res://Assets/Sprites/Characters/{prefix}_idle_west.png");
        _texMoveEast = LoadSpriteOrNull($"res://Assets/Sprites/Characters/{prefix}_move_east.png");
        _texMoveWest = LoadSpriteOrNull($"res://Assets/Sprites/Characters/{prefix}_move_west.png");
        _texChargeSouth = LoadSpriteOrNull($"res://Assets/Sprites/Characters/{prefix}_charge_south.png");
        _texChargeEast = LoadSpriteOrNull($"res://Assets/Sprites/Characters/{prefix}_charge_east.png");
        _texChargeWest = LoadSpriteOrNull($"res://Assets/Sprites/Characters/{prefix}_charge_west.png");
    }

    private static Texture2D? LoadSpriteOrNull(string path)
    {
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    public void SampleLocalInput()
    {
        if (IsBot)
        {
            return;
        }

        bool left;
        bool right;
        bool jumpHeld;

        if (IsLocalSecondaryKeyboard)
        {
            left = Input.IsKeyPressed(Key.J) || Input.IsKeyPressed(Key.Left);
            right = Input.IsKeyPressed(Key.L) || Input.IsKeyPressed(Key.Right);
            jumpHeld = Input.IsKeyPressed(Key.I) || Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.Enter);
        }
        else
        {
            bool useSharedArrows = GameSession.Instance.CurrentMode != GameMode.LocalTwoPlayerTest;
            left = Input.IsKeyPressed(Key.A) || (useSharedArrows && Input.IsKeyPressed(Key.Left));
            right = Input.IsKeyPressed(Key.D) || (useSharedArrows && Input.IsKeyPressed(Key.Right));
            jumpHeld = Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.W) || (useSharedArrows && Input.IsKeyPressed(Key.Up));
        }

        // GDD 1.2.1: A => Left (-1), D => Right (+1), neither or A+D => Straight (0)
        if (left && !right)
        {
            NetMoveInput = -1f;
        }
        else if (right && !left)
        {
            NetMoveInput = 1f;
        }
        else
        {
            NetMoveInput = 0f;
        }

        if (NetJumpHeld && !jumpHeld)
        {
            NetJumpReleasedBuffer = true;
        }
        NetJumpHeld = jumpHeld;
    }

    public void SimulateTick(float dt, bool isAuthoritativeHost)
    {
        if (IsEliminated)
        {
            Visible = false;
            _collisionShape.Disabled = true;
            return;
        }

        Visible = true;
        _collisionShape.Disabled = false;

        if (_bounceSfxCooldown > 0f)
        {
            _bounceSfxCooldown -= dt;
        }

        // Update active Buff / Debuff timers
        if (KineticNullifierRemaining > 0f)
        {
            KineticNullifierRemaining = Mathf.Max(0f, KineticNullifierRemaining - dt);
        }
        if (DirectionLockRemaining > 0f)
        {
            DirectionLockRemaining = Mathf.Max(0f, DirectionLockRemaining - dt);
        }

        if (!isAuthoritativeHost)
        {
            UpdateSpritePose();
            QueueRedraw();
            return;
        }

        // Respawn input lock check (GDD 1.2.1: after respawn, player must release jump and press again to charge)
        if (RequireJumpReleaseAfterRespawn)
        {
            if (!NetJumpHeld)
            {
                RequireJumpReleaseAfterRespawn = false;
                NetJumpReleasedBuffer = false;
            }
        }

        int moveDir = NetMoveInput < -0.2f ? -1 : (NetMoveInput > 0.2f ? 1 : 0);

        switch (CurrentState)
        {
            case PlayerState.Landing:
                _landingTimer -= dt;
                Velocity = Vector2.Zero;
                if (_landingTimer <= 0f)
                {
                    CurrentState = PlayerState.GroundedIdle;
                }
                break;

            case PlayerState.GroundedIdle:
            case PlayerState.GroundedMove:
                if (!CheckIsGrounded())
                {
                    CurrentState = PlayerState.Airborne;
                    break;
                }

                if (NetJumpHeld && !RequireJumpReleaseAfterRespawn)
                {
                    // Enter Charging immediately and stop walking (GDD 1.2.1 & T02)
                    CurrentState = PlayerState.Charging;
                    CurrentChargeTime = 0f;
                    _rawChargeElapsed = 0f;
                    IsChargeAscending = true;
                    Velocity = Vector2.Zero;
                    IntendedJumpDirection = moveDir;
                    if (moveDir != 0)
                    {
                        FacingDirection = moveDir;
                    }
                    GameSession.Instance.PlaySfx("sfx_charge", 0.9f, 0.45f);
                }
                else
                {
                    // Grounded walk (T01)
                    Velocity = new Vector2(moveDir * Tuning.WalkSpeed, 0f);
                    if (moveDir != 0)
                    {
                        FacingDirection = moveDir;
                        CurrentState = PlayerState.GroundedMove;
                    }
                    else
                    {
                        CurrentState = PlayerState.GroundedIdle;
                    }
                }
                break;

            case PlayerState.Charging:
                Velocity = Vector2.Zero;
                if (!CheckIsGrounded())
                {
                    CurrentChargeTime = 0f;
                    _rawChargeElapsed = 0f;
                    IsChargeAscending = true;
                    CurrentState = PlayerState.Airborne;
                    break;
                }

                // A/D during Charging changes intended direction without resetting charge (GDD 1.2.1 & T02)
                IntendedJumpDirection = moveDir;
                if (moveDir != 0)
                {
                    FacingDirection = moveDir;
                }

                // GDD v0.9.5 Section 1.2.1 & Slide 09, 20: Tụ lực tuần hoàn: Min <-> Max
                // Giữ: Min -> Max -> Min -> Max... Nhả: dùng đúng lực tại thời điểm đó (pha tăng hoặc giảm đều hợp lệ)
                float maxT = Tuning.MaxChargeTime > 0.001f ? Tuning.MaxChargeTime : 0.60f;
                _rawChargeElapsed += dt;
                float cycle = 2f * maxT;
                float phase = _rawChargeElapsed % cycle;
                bool wasAscending = IsChargeAscending;

                if (phase <= maxT)
                {
                    CurrentChargeTime = phase;
                    IsChargeAscending = true;
                }
                else
                {
                    CurrentChargeTime = cycle - phase;
                    IsChargeAscending = false;
                }

                // Audio feedback as game feedback (GDD v0.9.5 Slide 20: charge_up -> peak -> charge_down -> min)
                if (wasAscending && !IsChargeAscending)
                {
                    GameSession.Instance.PlaySfx("sfx_charge", 1.4f, 0.4f);
                }
                else if (!wasAscending && IsChargeAscending)
                {
                    GameSession.Instance.PlaySfx("sfx_charge", 0.85f, 0.35f);
                }

                if (NetJumpReleasedBuffer || !NetJumpHeld)
                {
                    NetJumpReleasedBuffer = false;
                    ExecuteJumpRelease();
                }
                break;

            case PlayerState.Airborne:
                NetJumpReleasedBuffer = false;
                // NO air control, NO double jump (GDD 1.2.1 & T03)
                float newVy = Mathf.Min(Tuning.MaxFallSpeed, Velocity.Y + Tuning.Gravity * dt);
                Velocity = new Vector2(Velocity.X, newVy);
                break;
        }

        PerformPhysicsStep(dt);

        if (GlobalPosition.Y < BestAltitudeReached)
        {
            BestAltitudeReached = GlobalPosition.Y;
        }

        UpdateSpritePose();
        QueueRedraw();
    }

    private void ExecuteJumpRelease()
    {
        float ratio = ChargeRatio;
        float jumpSpeed = Mathf.Lerp(Tuning.MinJumpSpeed, Tuning.MaxJumpSpeed, ratio);

        int finalDir = IntendedJumpDirection;
        // Direction Lock debuff handling (GDD 1.2.3 & T08: clear fallback when locked direction is chosen)
        if (HasDirectionLock && finalDir == LockedJumpDirection)
        {
            // Fallback: if Left/Right is locked, jump Straight (0); if Straight (0) is locked, jump in facing direction
            finalDir = LockedJumpDirection == 0 ? FacingDirection : 0;
        }

        float vx = finalDir * Tuning.HorizontalJumpSpeed;
        float vy = -jumpSpeed;

        if (finalDir != 0)
        {
            FacingDirection = finalDir;
        }

        Velocity = new Vector2(vx, vy);
        CurrentChargeTime = 0f;
        _rawChargeElapsed = 0f;
        IsChargeAscending = true;
        CurrentState = PlayerState.Airborne;
        GameSession.Instance.PlaySfx("sfx_jump", Mathf.Lerp(0.9f, 1.25f, ratio), 0.75f);
    }

    private void PerformPhysicsStep(float dt)
    {
        Vector2 remainingMotion = Velocity * dt;
        const int maxBounces = 3;

        for (int i = 0; i < maxBounces && remainingMotion.LengthSquared() > 0.0001f; i++)
        {
            var collision = MoveAndCollide(remainingMotion);
            if (collision == null)
            {
                break;
            }

            Vector2 normal = collision.GetNormal();
            var colliderNode = collision.GetCollider() as Node;

            // 1. Check for Player vs Player collision (GDD 1.3.1 & CollisionResolver)
            if (colliderNode is PlayerController otherPlayer && !otherPlayer.IsEliminated)
            {
                CollisionResolver.TryResolvePlayerPair(this, otherPlayer, normal, Tuning);
                remainingMotion = remainingMotion.Slide(normal);
                continue;
            }

            // 2. Check for Platform vs Wall / Ceiling collision
            if (CurrentState == PlayerState.Airborne)
            {
                bool isBounceWall = colliderNode != null && colliderNode.IsInGroup("BounceWall");
                Vector2 newVel = CollisionResolver.ResolveTerrainBounce(
                    Velocity,
                    normal,
                    isBounceWall,
                    Tuning,
                    out bool isLanding,
                    out bool bouncedOffWallOrCeiling);

                if (isLanding)
                {
                    Velocity = Vector2.Zero;
                    CurrentState = PlayerState.Landing;
                    _landingTimer = Tuning.LandingRecoveryTime;
                    remainingMotion = Vector2.Zero;
                    break;
                }

                if (bouncedOffWallOrCeiling)
                {
                    Velocity = newVel;
                    if (_bounceSfxCooldown <= 0f)
                    {
                        _bounceSfxCooldown = 0.10f;
                        GameSession.Instance.PlaySfx("sfx_bounce", 0.9f, 0.7f);
                    }
                    UpdateAirborneFacingFromVelocity();
                    remainingMotion = remainingMotion.Bounce(normal) * Tuning.WallRestitution;
                }
            }
            else
            {
                // Grounded walk sliding along platform or stopping against wall
                if (Mathf.Abs(normal.X) > 0.7f)
                {
                    Velocity = new Vector2(0f, Velocity.Y);
                    break;
                }
                remainingMotion = remainingMotion.Slide(normal);
            }
        }
    }

    private bool CheckIsGrounded()
    {
        return TestMove(GlobalTransform, new Vector2(0f, 2.0f));
    }

    public void ApplyExternalImpulse(Vector2 impulseVelocityDelta)
    {
        Velocity += impulseVelocityDelta;
        Velocity = new Vector2(
            Mathf.Clamp(Velocity.X, -420f, 420f),
            Mathf.Clamp(Velocity.Y, -Tuning.MaxJumpSpeed * 1.15f, Tuning.MaxFallSpeed));

        if (Velocity.Y < -20f || !CheckIsGrounded())
        {
            CurrentChargeTime = 0f;
            _rawChargeElapsed = 0f;
            IsChargeAscending = true;
            CurrentState = PlayerState.Airborne;
        }

        UpdateAirborneFacingFromVelocity();
    }

    private void UpdateAirborneFacingFromVelocity()
    {
        // GDD 1.3.1 & T03: In air, only flip FacingDirection if |vx| > FacingVelocityEpsilon
        if (Mathf.Abs(Velocity.X) > Tuning.FacingVelocityEpsilon)
        {
            FacingDirection = Velocity.X > 0f ? 1 : -1;
        }
    }

    public void ActivateKineticNullifier(float duration)
    {
        KineticNullifierRemaining = duration;
        GameSession.Instance.PlaySfx("sfx_item", 1.2f, 0.8f);
    }

    public void ApplyDirectionLock(int lockedDir, float duration)
    {
        // GDD 1.2.3 & T08: Does not stack; new replaces old; persists across respawn
        LockedJumpDirection = Mathf.Clamp(lockedDir, -1, 1);
        DirectionLockRemaining = duration;
        GameSession.Instance.PlaySfx("sfx_item", 0.75f, 0.85f);
    }

    public void RespawnAt(Vector2 targetPoint)
    {
        // GDD 1.2.1 & 1.3.3: Reset velocity & charge to 0, require jump release before charging again, KEEP debuff!
        GlobalPosition = targetPoint;
        Velocity = Vector2.Zero;
        CurrentChargeTime = 0f;
        _rawChargeElapsed = 0f;
        IsChargeAscending = true;
        CurrentState = PlayerState.GroundedIdle;
        RequireJumpReleaseAfterRespawn = true;
        NetJumpReleasedBuffer = false;
    }

    private void UpdateSpritePose()
    {
        if (_sprite == null)
        {
            return;
        }

        bool faceRight = FacingDirection >= 0;

        switch (CurrentState)
        {
            case PlayerState.Charging:
                if (IntendedJumpDirection == 0 && _texChargeSouth != null)
                {
                    _sprite.Texture = _texChargeSouth;
                }
                else
                {
                    _sprite.Texture = faceRight ? _texChargeEast : _texChargeWest;
                }
                break;

            case PlayerState.GroundedMove:
                _sprite.Texture = faceRight ? _texMoveEast : _texMoveWest;
                break;

            case PlayerState.Airborne:
            case PlayerState.GroundedIdle:
            case PlayerState.Landing:
            default:
                // GDD 1.3.1 & 1.6.2: Airborne temporarily uses Idle pose with facing updated by bounce velocity
                _sprite.Texture = faceRight ? _texIdleEast : (_texIdleWest ?? _texIdleSouth);
                break;
        }
    }

    public override void _Draw()
    {
        if (IsEliminated)
        {
            return;
        }

        // 1. Draw P1-P4 Tag Badge above character head (GDD Section 1.5.4)
        Vector2 badgePos = new(-11f, -42f);
        DrawRect(new Rect2(badgePos, new Vector2(22f, 10f)), new Color(0.05f, 0.07f, 0.12f, 0.9f), true);
        DrawRect(new Rect2(badgePos, new Vector2(22f, 10f)), TagColor, false, 1.0f);
        if (ThemeDB.FallbackFont != null)
        {
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(-7f, -34f),
                PlayerTag,
                HorizontalAlignment.Center,
                -1f,
                8,
                TagColor);
        }

        // 2. Draw Charge Bar + Cyclic Indicator Arrow when Charging (GDD v0.9.5 Slide 9)
        if (CurrentState == PlayerState.Charging)
        {
            float ratio = ChargeRatio;
            Vector2 barPos = new(-12f, -30f);
            DrawRect(new Rect2(barPos, new Vector2(24f, 4f)), new Color(0f, 0f, 0f, 0.85f), true);
            Color chargeCol = ratio >= 0.98f ? new Color("ff595e") : new Color("ffca3a").Lerp(new Color("ff595e"), ratio);
            DrawRect(new Rect2(barPos + new Vector2(1f, 1f), new Vector2(22f * ratio, 2f)), chargeCol, true);

            // Cyclic indicator arrow (GDD v0.9.5 Slide 9: Min <-> Max oscillating)
            string cycleStr = IsChargeAscending ? "▲" : "▼";
            Color cycleCol = IsChargeAscending ? new Color("8ecae6") : new Color("ffb703");
            if (ThemeDB.FallbackFont != null)
            {
                DrawString(ThemeDB.FallbackFont, new Vector2(14f, -27f), cycleStr, HorizontalAlignment.Left, -1f, 7, cycleCol);
            }

            // Optional Jump King Parabola trajectory preview (from reference [4])
            if (GameSession.Instance.ShowTrajectoryPreview)
            {
                DrawJumpParabolaPreview(ratio, chargeCol);
            }
        }

        // 3. Draw Kinetic Nullifier Shield aura when active (GDD 1.2.3)
        if (HasKineticNullifier)
        {
            DrawArc(new Vector2(0f, -12f), 15f, 0f, Mathf.Tau, 24, new Color(0.25f, 0.85f, 1.0f, 0.85f), 1.5f);
        }

        // 4. Draw Direction Lock warning badge when active (GDD 1.2.3)
        if (HasDirectionLock)
        {
            string lockText = LockedJumpDirection < 0 ? "X←" : (LockedJumpDirection > 0 ? "X→" : "X↑");
            if (ThemeDB.FallbackFont != null)
            {
                DrawString(
                    ThemeDB.FallbackFont,
                    new Vector2(-8f, -45f),
                    lockText,
                    HorizontalAlignment.Center,
                    -1f,
                    8,
                    new Color("ff4d6d"));
            }
        }
    }

    private void DrawJumpParabolaPreview(float chargeRatio, Color color)
    {
        int dir = IntendedJumpDirection;
        if (HasDirectionLock && dir == LockedJumpDirection)
        {
            dir = LockedJumpDirection == 0 ? FacingDirection : 0;
        }

        float jumpSpeed = Mathf.Lerp(Tuning.MinJumpSpeed, Tuning.MaxJumpSpeed, chargeRatio);
        Vector2 simVel = new(dir * Tuning.HorizontalJumpSpeed, -jumpSpeed);
        Vector2 simPos = new(0f, -12f);
        const float stepDt = 1.0f / 30.0f;

        for (int i = 1; i <= 16; i++)
        {
            simPos += simVel * stepDt;
            simVel.Y = Mathf.Min(Tuning.MaxFallSpeed, simVel.Y + Tuning.Gravity * stepDt);
            if (i % 2 == 0)
            {
                DrawCircle(simPos, 1.5f, new Color(color.R, color.G, color.B, 0.75f));
            }
        }
    }
}
