using Godot;

namespace BounceBash.Config;

/// <summary>
/// Godot Resource equivalent of Unity's ScriptableObject (GDD Section 1.3.2 & 1.7.2).
/// Centralizes all physics, camera, respawn, match, and item tuning parameters
/// to avoid magic numbers across gameplay scripts.
/// Reference conversion from Jump King Parabole mod (60 FPS -> per-second):
/// - MaxChargeTime = 0.60s
/// - MaxJumpSpeed = 8.742858 * 60 = 524.57 px/s
/// - HorizontalJumpSpeed = 3.5 * 60 = 210.0 px/s
/// - Gravity = 0.2571429 * 3600 = 925.71 px/s^2
/// - MaxFallSpeed = 10.0 * 60 = 600.0 px/s
/// </summary>
[GlobalClass]
public partial class TuningConfig : Resource
{
    [ExportGroup("Movement & Gravity")]
    [Export] public float WalkSpeed { get; set; } = 95.0f;
    [Export] public float Gravity { get; set; } = 925.71f;
    [Export] public float MaxFallSpeed { get; set; } = 600.0f;

    [ExportGroup("Jump & Charging (Jump King Style)")]
    [Export] public float MaxChargeTime { get; set; } = 0.60f;
    [Export] public float MinJumpSpeed { get; set; } = 145.0f;
    [Export] public float MaxJumpSpeed { get; set; } = 524.57f;
    [Export] public float HorizontalJumpSpeed { get; set; } = 210.0f;
    [Export] public float LandingRecoveryTime { get; set; } = 0.05f;

    [ExportGroup("Physics & Collision Restitution")]
    [Export] public float PlayerMass { get; set; } = 1.0f;
    [Export] public float WallRestitution { get; set; } = 0.72f;
    [Export] public float CeilingRestitution { get; set; } = 0.45f;
    [Export] public float PlayerRestitution { get; set; } = 0.88f;
    [Export] public float GroundFriction { get; set; } = 0.85f;
    [Export] public float MinWallBounceSpeed { get; set; } = 65.0f;

    [ExportGroup("Visual Readability")]
    [Export] public float FacingVelocityEpsilon { get; set; } = 8.0f;

    [ExportGroup("Camera & Respawn (Main/Aux)")]
    [Export] public float SinglePlayerCameraScrollSpeed { get; set; } = 16.0f;
    [Export] public float PvPCameraSmoothSpeed { get; set; } = 8.0f;
    [Export] public float RespawnSafeMarginBottom { get; set; } = 48.0f;
    [Export] public int InitialLives { get; set; } = 3;

    [ExportGroup("Match Rules & GoalZone")]
    [Export] public float GoalHoldTimeSeconds { get; set; } = 5.0f;
    [Export] public float QuickMatchDurationSeconds { get; set; } = 180.0f;

    [ExportGroup("Items & Buffs/Debuffs")]
    [Export] public float KineticNullifierDuration { get; set; } = 8.0f;
    [Export] public float KineticNullifierRespawnDelay { get; set; } = 6.0f;
    [Export] public float DirectionLockDuration { get; set; } = 10.0f;
}
