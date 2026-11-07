using System.Collections.Generic;
using BounceBash.Config;
using BounceBash.Core;
using BounceBash.Player;
using Godot;

namespace BounceBash.Camera;

/// <summary>
/// Camera controller implementing GDD Section 1.2.4 & Test T06:
/// - Singleplayer: Continuous upward scroll at SinglePlayerCameraScrollSpeed (never stops waiting),
///   clamped at the Temple roof so GoalZone never disappears off-screen.
/// - PvP (2-4 players): Shared camera following the highest non-eliminated player,
///   monotonic upward only while climbing, with smart recovery descent if the previous leader is eliminated.
/// </summary>
public partial class CameraRig : Camera2D
{
    public const float ViewportWidth = 640.0f;
    public const float ViewportHeight = 360.0f;

    public float TopEdgeY => GlobalPosition.Y - ViewportHeight * 0.5f;
    public float BottomEdgeY => GlobalPosition.Y + ViewportHeight * 0.5f;

    private float _highestCenterY;
    private float _minCenterYLimit = -2160.0f + ViewportHeight * 0.5f;
    private TuningConfig Tuning => GameSession.Instance.Tuning;

    public override void _Ready()
    {
        Enabled = true;
        PositionSmoothingEnabled = false;
    }

    public void ResetCamera(float startBottomY, float towerTopY)
    {
        _highestCenterY = startBottomY - ViewportHeight * 0.5f;
        // Clamp top of camera so GoalZone in Sector 3 remains well inside the frame (GDD 1.4.2 & 1.4.4)
        _minCenterYLimit = towerTopY + ViewportHeight * 0.5f;
        GlobalPosition = new Vector2(ViewportWidth * 0.5f, _highestCenterY);
    }

    public void UpdateCamera(float dt, IReadOnlyList<PlayerController> players, GameMode mode)
    {
        if (mode == GameMode.SinglePlayer)
        {
            // Continuous upward auto-scroll, plus catches up if the single player climbs faster than scroll speed
            float nextY = _highestCenterY - Tuning.SinglePlayerCameraScrollSpeed * dt;
            foreach (var p in players)
            {
                if (!p.IsEliminated)
                {
                    float desiredPlayerCenterY = p.GlobalPosition.Y - 30.0f;
                    if (desiredPlayerCenterY < nextY)
                    {
                        nextY = Mathf.Lerp(nextY, desiredPlayerCenterY, Mathf.Clamp(dt * Tuning.PvPCameraSmoothSpeed, 0f, 1f));
                    }
                }
            }

            _highestCenterY = Mathf.Max(_minCenterYLimit, Mathf.Min(_highestCenterY, nextY));
        }
        else
        {
            // PvP (2-4 players): Find highest non-eliminated player (minimum Y in Godot 2D)
            float leaderY = float.MaxValue;
            foreach (var p in players)
            {
                if (!p.IsEliminated && p.GlobalPosition.Y < leaderY)
                {
                    leaderY = p.GlobalPosition.Y;
                }
            }

            if (leaderY < float.MaxValue)
            {
                float desiredCenterY = leaderY - 24.0f;
                // If the previous leader was eliminated and all remaining survivors are below current camera view,
                // smoothly descend the camera to the new surviving leader so remaining players aren't killed by an empty camera.
                if (leaderY > BottomEdgeY - 48.0f)
                {
                    _highestCenterY = Mathf.Lerp(
                        _highestCenterY,
                        desiredCenterY,
                        Mathf.Clamp(dt * Tuning.PvPCameraSmoothSpeed * 2.5f, 0f, 1f));
                }
                else if (desiredCenterY < _highestCenterY)
                {
                    // Monotonic upward rule (GDD 1.2.4 & T06): camera only moves up (smaller Y) or stays still
                    float smoothed = Mathf.Lerp(
                        _highestCenterY,
                        desiredCenterY,
                        Mathf.Clamp(dt * Tuning.PvPCameraSmoothSpeed, 0f, 1f));
                    _highestCenterY = Mathf.Max(_minCenterYLimit, Mathf.Min(_highestCenterY, smoothed));
                }
            }
        }

        GlobalPosition = new Vector2(ViewportWidth * 0.5f, _highestCenterY);
        QueueRedraw();
    }

    public void ApplyAuthoritativeCameraY(float centerY)
    {
        _highestCenterY = centerY;
        GlobalPosition = new Vector2(ViewportWidth * 0.5f, _highestCenterY);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Debug safe margin indicator line in editor
        if (OS.IsDebugBuild())
        {
            float safeY = ViewportHeight * 0.5f - Tuning.RespawnSafeMarginBottom;
            DrawLine(new Vector2(-ViewportWidth * 0.5f, safeY), new Vector2(ViewportWidth * 0.5f, safeY), new Color(1f, 0.4f, 0.2f, 0.35f), 1.0f);
        }
    }
}
