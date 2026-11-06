using System.Collections.Generic;
using BounceBash.Config;
using BounceBash.Core;
using BounceBash.Level;
using BounceBash.Player;
using Godot;

namespace BounceBash.Gameplay;

public class SpawnedWorldItem
{
    public int ItemId { get; set; }
    public Vector2 Position { get; set; }
    public ItemEffectType EffectType { get; set; }
    public int LockedDirection { get; set; } // -1 = Left, 0 = Straight, +1 = Right
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Manages Buff (Kinetic Nullifier), Debuff (Direction Lock), and Trap anchors (GDD Section 1.2.3 & T08):
/// - Kinetic Nullifier: Only ONE instance can exist/be active at a time; next spawn wave is only
///   considered after the previous effect ends.
/// - Direction Lock: Uses 3 distinct icon variants (Left, Straight, Right) matching GDD Table 1.2-01.
/// </summary>
public partial class ItemSystem : Node2D
{
    public List<SpawnedWorldItem> WorldItems { get; } = [];

    private Texture2D? _iconKineticNullifier;
    private Texture2D? _iconDirLockLeft;
    private Texture2D? _iconDirLockStraight;
    private Texture2D? _iconDirLockRight;

    private float _nullifierRespawnCooldown;
    private int _nextNullifierAnchorCursor;
    private readonly List<ItemAnchorCandidate> _nullifierAnchors = [];

    private TuningConfig Tuning => GameSession.Instance.Tuning;

    public override void _Ready()
    {
        _iconKineticNullifier = LoadIcon("res://Assets/Sprites/Icons/icon_kinetic_nullifier.png");
        _iconDirLockLeft = LoadIcon("res://Assets/Sprites/Icons/icon_direction_lock_left.png");
        _iconDirLockStraight = LoadIcon("res://Assets/Sprites/Icons/icon_direction_lock_straight.png");
        _iconDirLockRight = LoadIcon("res://Assets/Sprites/Icons/icon_direction_lock_right.png");
    }

    private static Texture2D? LoadIcon(string path)
    {
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    public void InitializeItemsFromAnchors(IReadOnlyList<ItemAnchorCandidate> anchors)
    {
        WorldItems.Clear();
        _nullifierAnchors.Clear();
        _nextNullifierAnchorCursor = 0;
        _nullifierRespawnCooldown = 0f;

        int idCounter = 1;
        foreach (var anchor in anchors)
        {
            if (anchor.PreferredType == ItemEffectType.KineticNullifier)
            {
                _nullifierAnchors.Add(anchor);
            }
            else if (anchor.PreferredType == ItemEffectType.DirectionLock)
            {
                WorldItems.Add(new SpawnedWorldItem
                {
                    ItemId = idCounter++,
                    Position = anchor.GlobalPos,
                    EffectType = ItemEffectType.DirectionLock,
                    LockedDirection = anchor.LockedDirectionVariant,
                    IsActive = true
                });
            }
        }

        // Spawn the single unique Kinetic Nullifier at the first candidate anchor (GDD 1.2.3)
        if (_nullifierAnchors.Count > 0)
        {
            var first = _nullifierAnchors[0];
            _nextNullifierAnchorCursor = 1 % _nullifierAnchors.Count;
            WorldItems.Add(new SpawnedWorldItem
            {
                ItemId = idCounter,
                Position = first.GlobalPos,
                EffectType = ItemEffectType.KineticNullifier,
                LockedDirection = 0,
                IsActive = true
            });
        }

        QueueRedraw();
    }

    public void UpdateItems(float dt, IReadOnlyList<PlayerController> players)
    {
        bool anyNullifierSpawnedInWorld = false;
        bool anyPlayerHasNullifierActive = false;

        foreach (var p in players)
        {
            if (!p.IsEliminated && p.HasKineticNullifier)
            {
                anyPlayerHasNullifierActive = true;
                break;
            }
        }

        bool changed = false;
        foreach (var item in WorldItems)
        {
            if (!item.IsActive)
            {
                continue;
            }

            if (item.EffectType == ItemEffectType.KineticNullifier)
            {
                anyNullifierSpawnedInWorld = true;
            }

            foreach (var player in players)
            {
                if (player.IsEliminated)
                {
                    continue;
                }

                Vector2 playerCenter = player.GlobalPosition + new Vector2(0f, -12f);
                if (playerCenter.DistanceSquaredTo(item.Position) <= 20f * 20f)
                {
                    item.IsActive = false;
                    changed = true;

                    if (item.EffectType == ItemEffectType.KineticNullifier)
                    {
                        player.ActivateKineticNullifier(Tuning.KineticNullifierDuration);
                        _nullifierRespawnCooldown = Tuning.KineticNullifierRespawnDelay;
                    }
                    else if (item.EffectType == ItemEffectType.DirectionLock)
                    {
                        player.ApplyDirectionLock(item.LockedDirection, Tuning.DirectionLockDuration);
                    }
                    break;
                }
            }
        }

        // GDD 1.2.3: Next Kinetic Nullifier spawn is only evaluated AFTER the previous effect ends
        if (!anyNullifierSpawnedInWorld && !anyPlayerHasNullifierActive && _nullifierAnchors.Count > 0)
        {
            _nullifierRespawnCooldown -= dt;
            if (_nullifierRespawnCooldown <= 0f)
            {
                var nextAnchor = _nullifierAnchors[_nextNullifierAnchorCursor];
                _nextNullifierAnchorCursor = (_nextNullifierAnchorCursor + 1) % _nullifierAnchors.Count;

                foreach (var item in WorldItems)
                {
                    if (item.EffectType == ItemEffectType.KineticNullifier)
                    {
                        item.Position = nextAnchor.GlobalPos;
                        item.IsActive = true;
                        changed = true;
                        break;
                    }
                }
            }
        }

        if (changed)
        {
            QueueRedraw();
        }
    }

    public Texture2D? GetDirectionLockIcon(int lockedDirection)
    {
        return lockedDirection switch
        {
            < 0 => _iconDirLockLeft,
            > 0 => _iconDirLockRight,
            _ => _iconDirLockStraight
        };
    }

    public Texture2D? GetKineticNullifierIcon() => _iconKineticNullifier;

    public override void _Draw()
    {
        foreach (var item in WorldItems)
        {
            if (!item.IsActive)
            {
                continue;
            }

            Texture2D? tex = item.EffectType == ItemEffectType.KineticNullifier
                ? _iconKineticNullifier
                : GetDirectionLockIcon(item.LockedDirection);

            if (tex != null)
            {
                Rect2 dst = new(item.Position - new Vector2(11f, 11f), new Vector2(22f, 22f));
                DrawTextureRect(tex, dst, false);
            }
            else
            {
                Color c = item.EffectType == ItemEffectType.KineticNullifier
                    ? new Color("00bbf9")
                    : new Color("f15bb5");
                DrawCircle(item.Position, 8f, c);
            }
        }
    }
}
