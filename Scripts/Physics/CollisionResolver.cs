using System.Collections.Generic;
using BounceBash.Config;
using BounceBash.Player;
using Godot;

namespace BounceBash.Physics;

/// <summary>
/// Deterministic collision & momentum transfer solver (GDD Section 1.3.1 & T04).
/// Uses the ideal impulse formula:
///   J = -(1 + e) * (v_rel . n) / (1/m1 + 1/m2)
/// Ensures each player-player pair is resolved at most once per physics tick
/// so double-impulse never occurs between engine and script.
/// Also respects the Kinetic Nullifier buff (GDD Section 1.2.3 & T08).
/// </summary>
public static class CollisionResolver
{
    private static ulong _lastResolvedFrame;
    private static readonly HashSet<long> _resolvedPairsThisTick = [];

    public static void BeginPhysicsTick(ulong physicsFrame)
    {
        if (_lastResolvedFrame != physicsFrame)
        {
            _lastResolvedFrame = physicsFrame;
            _resolvedPairsThisTick.Clear();
        }
    }

    public static bool TryResolvePlayerPair(
        PlayerController playerA,
        PlayerController playerB,
        Vector2 contactNormalFromBToA,
        TuningConfig tuning)
    {
        BeginPhysicsTick(Engine.GetPhysicsFrames());

        int idA = playerA.SlotIndex;
        int idB = playerB.SlotIndex;
        long pairKey = idA < idB ? ((long)idA << 32) | (uint)idB : ((long)idB << 32) | (uint)idA;

        if (!_resolvedPairsThisTick.Add(pairKey))
        {
            return false;
        }

        Vector2 n = contactNormalFromBToA.Normalized();
        if (n.LengthSquared() < 0.001f)
        {
            n = (playerA.GlobalPosition - playerB.GlobalPosition).Normalized();
            if (n.LengthSquared() < 0.001f)
            {
                n = Vector2.Up;
            }
        }

        // Ensure normal points from B toward A
        if ((playerA.GlobalPosition - playerB.GlobalPosition).Dot(n) < 0f)
        {
            n = -n;
        }

        Vector2 vRel = playerA.Velocity - playerB.Velocity;
        float velAlongNormal = vRel.Dot(n);

        // Do not resolve if already separating
        if (velAlongNormal > 0f)
        {
            return false;
        }

        float m1 = Mathf.Max(0.1f, tuning.PlayerMass);
        float m2 = Mathf.Max(0.1f, tuning.PlayerMass);
        float e = Mathf.Clamp(tuning.PlayerRestitution, 0.1f, 1.2f);

        float j = -(1.0f + e) * velAlongNormal / ((1.0f / m1) + (1.0f / m2));
        // Enforce a minimum separation impulse for crisp arcade feel
        j = Mathf.Max(j, 110.0f);

        Vector2 impulseOnA = (j / m1) * n;
        Vector2 impulseOnB = -(j / m2) * n;

        // Kinetic Nullifier (GDD 1.2.3): blocks incoming opponent impulse while keeping own velocity
        if (!playerA.HasKineticNullifier)
        {
            playerA.ApplyExternalImpulse(impulseOnA);
        }

        if (!playerB.HasKineticNullifier)
        {
            playerB.ApplyExternalImpulse(impulseOnB);
        }

        return true;
    }

    public static Vector2 ResolveTerrainBounce(
        Vector2 preCollisionVelocity,
        Vector2 normal,
        bool isBounceWall,
        TuningConfig tuning,
        out bool landedOnGround,
        out bool bouncedOffWallOrCeiling)
    {
        landedOnGround = false;
        bouncedOffWallOrCeiling = false;

        // 1. Floor / Landing check (normal pointing mostly upward in Godot 2D: Y < -0.65)
        if (normal.Y < -0.65f && preCollisionVelocity.Y >= 0f)
        {
            landedOnGround = true;
            return new Vector2(0f, 0f);
        }

        // 2. Wall bounce check (|normal.X| >= 0.5)
        if (Mathf.Abs(normal.X) >= 0.5f)
        {
            bouncedOffWallOrCeiling = true;
            float restitution = isBounceWall ? tuning.WallRestitution : tuning.WallRestitution * 0.8f;
            float reflectedVx = -preCollisionVelocity.X * restitution;
            if (Mathf.Abs(reflectedVx) < tuning.MinWallBounceSpeed)
            {
                reflectedVx = Mathf.Sign(normal.X) * tuning.MinWallBounceSpeed;
            }
            // Gravity continues acting on Y after wall bounce (GDD 1.3.1 & T04)
            return new Vector2(reflectedVx, preCollisionVelocity.Y * 0.95f);
        }

        // 3. Ceiling bounce check (normal pointing downward: Y > 0.65)
        if (normal.Y > 0.65f && preCollisionVelocity.Y < 0f)
        {
            bouncedOffWallOrCeiling = true;
            float reflectedVy = -preCollisionVelocity.Y * tuning.CeilingRestitution;
            return new Vector2(preCollisionVelocity.X * 0.9f, Mathf.Max(40f, reflectedVy));
        }

        // Default reflection fallback
        bouncedOffWallOrCeiling = true;
        return preCollisionVelocity.Bounce(normal) * tuning.WallRestitution;
    }
}
