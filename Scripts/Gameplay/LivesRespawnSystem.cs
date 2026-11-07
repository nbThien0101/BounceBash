using System.Collections.Generic;
using BounceBash.Camera;
using BounceBash.Config;
using BounceBash.Level;
using BounceBash.Player;
using Godot;

namespace BounceBash.Gameplay;

/// <summary>
/// Implements GDD Section 1.2.2, 1.3.3, and Test T07:
/// - Each player has 3 lives.
/// - Crossing below the bottom edge of the camera immediately deducts 1 life (no 2-second wait).
/// - Continuously updates each player's Main (primary) and Aux (backup) respawn platforms
///   relative to the current camera frame and leader altitude.
/// - If Main becomes invalid (e.g. falls below camera bottom safe margin), promotes Aux -> Main
///   and selects a new Aux.
/// - 1st and 2nd fall respawn at Main/Aux (resetting velocity/charge, keeping DirectionLock debuff).
/// - 3rd fall eliminates the player ("Kiệt sức").
/// </summary>
public static class LivesRespawnSystem
{
    public static void UpdateMainAuxAndCheckFalls(
        IReadOnlyList<PlayerController> players,
        IReadOnlyList<PlatformCandidate> platforms,
        CameraRig camera,
        TuningConfig tuning,
        bool isPvP)
    {
        float camTop = camera.TopEdgeY + 28.0f;
        float camBottomSafe = camera.BottomEdgeY - tuning.RespawnSafeMarginBottom;
        float camBottomOut = camera.BottomEdgeY + 12.0f;

        // Find leader Y (highest active player = minimum Y)
        float leaderY = float.MaxValue;
        foreach (var p in players)
        {
            if (!p.IsEliminated && p.GlobalPosition.Y < leaderY)
            {
                leaderY = p.GlobalPosition.Y;
            }
        }

        foreach (var player in players)
        {
            if (player.IsEliminated)
            {
                continue;
            }

            // 1. Validate existing Main and Aux platforms against current camera window & leader
            bool mainStillValid = player.HasValidMainRespawn &&
                                  IsPointValidForRespawn(player.MainRespawnPoint, camTop, camBottomSafe, leaderY, isPvP);
            bool auxStillValid = player.HasValidAuxRespawn &&
                                 IsPointValidForRespawn(player.AuxRespawnPoint, camTop, camBottomSafe, leaderY, isPvP);

            if (!mainStillValid && auxStillValid)
            {
                // Promote Aux -> Main when Main falls out of safe margin (GDD 1.3.3)
                player.MainRespawnPoint = player.AuxRespawnPoint;
                player.HasValidMainRespawn = true;
                player.HasValidAuxRespawn = false;
                mainStillValid = true;
            }

            // 2. Refresh Main / Aux from valid platform candidates inside camera
            RefreshPlayerMainAux(player, platforms, camTop, camBottomSafe, leaderY, isPvP);

            // 3. Check if player collider crossed below the bottom edge of camera (GDD 1.2.2 & T07)
            if (player.GlobalPosition.Y > camBottomOut)
            {
                HandlePlayerFallOutOfView(player, platforms, camera, leaderY, isPvP);
            }
        }
    }

    private static bool IsPointValidForRespawn(
        Vector2 point,
        float camTop,
        float camBottomSafe,
        float leaderY,
        bool isPvP)
    {
        if (point.Y < camTop || point.Y > camBottomSafe)
        {
            return false;
        }

        // GDD 1.3.3: In PvP, respawn platform cannot be higher (smaller Y) than the active leader
        if (isPvP && leaderY < float.MaxValue && point.Y < leaderY - 8.0f)
        {
            return false;
        }

        return true;
    }

    private static void RefreshPlayerMainAux(
        PlayerController player,
        IReadOnlyList<PlatformCandidate> platforms,
        float camTop,
        float camBottomSafe,
        float leaderY,
        bool isPvP)
    {
        PlatformCandidate? bestMain = null;
        PlatformCandidate? bestAux = null;
        float bestMainScore = float.MaxValue;
        float bestAuxScore = float.MaxValue;

        foreach (var cand in platforms)
        {
            if (cand.Width < PlayerController.ColliderWidth + 16.0f)
            {
                continue;
            }

            Vector2 spawnPt = cand.TopCenterGlobal;
            if (!IsPointValidForRespawn(spawnPt, camTop, camBottomSafe, leaderY, isPvP))
            {
                continue;
            }

            // Prefer platforms near or slightly below the player's current/recent progress inside the camera
            float distY = Mathf.Abs(spawnPt.Y - Mathf.Clamp(player.GlobalPosition.Y, camTop, camBottomSafe));
            if (distY < bestMainScore)
            {
                bestAux = bestMain;
                bestAuxScore = bestMainScore;
                bestMain = cand;
                bestMainScore = distY;
            }
            else if (distY < bestAuxScore && (!bestMain.HasValue || bestMain.Value.TopCenterGlobal != spawnPt))
            {
                bestAux = cand;
                bestAuxScore = distY;
            }
        }

        if (bestMain.HasValue)
        {
            player.MainRespawnPoint = bestMain.Value.TopCenterGlobal + new Vector2((player.SlotIndex - 1.5f) * 8f, 0f);
            player.HasValidMainRespawn = true;
        }

        if (bestAux.HasValue)
        {
            player.AuxRespawnPoint = bestAux.Value.TopCenterGlobal + new Vector2((player.SlotIndex - 1.5f) * 8f, 0f);
            player.HasValidAuxRespawn = true;
        }
    }

    private static void HandlePlayerFallOutOfView(
        PlayerController player,
        IReadOnlyList<PlatformCandidate> platforms,
        CameraRig camera,
        float leaderY,
        bool isPvP)
    {
        player.RemainingLives = Mathf.Max(0, player.RemainingLives - 1);
        Core.GameSession.Instance.PlaySfx("sfx_fall", 1.0f, 0.85f);

        // 3rd fall -> Eliminated due to exhaustion ("Kiệt sức", GDD 1.2.2 & 1.5.5)
        if (player.RemainingLives <= 0)
        {
            player.IsEliminated = true;
            player.Velocity = Vector2.Zero;
            player.Visible = false;
            // Place eliminated player safely below view to prevent collisions
            player.GlobalPosition = new Vector2(player.GlobalPosition.X, camera.BottomEdgeY + 2000.0f);
            return;
        }

        // 1st or 2nd fall -> Respawn at Main, or Aux, or safe fallback platform inside camera
        Vector2 targetPoint;
        if (player.HasValidMainRespawn)
        {
            targetPoint = player.MainRespawnPoint;
        }
        else if (player.HasValidAuxRespawn)
        {
            targetPoint = player.AuxRespawnPoint;
        }
        else
        {
            targetPoint = FindEmergencyPlatformInCamera(platforms, camera, leaderY, isPvP, player.SlotIndex);
        }

        player.RespawnAt(targetPoint);
    }

    private static Vector2 FindEmergencyPlatformInCamera(
        IReadOnlyList<PlatformCandidate> platforms,
        CameraRig camera,
        float leaderY,
        bool isPvP,
        int slotIndex)
    {
        float midY = camera.GlobalPosition.Y;
        PlatformCandidate? closest = null;
        float bestDist = float.MaxValue;

        foreach (var cand in platforms)
        {
            if (cand.TopCenterGlobal.Y <= camera.BottomEdgeY - 24.0f &&
                cand.TopCenterGlobal.Y >= camera.TopEdgeY + 20.0f)
            {
                if (isPvP && leaderY < float.MaxValue && cand.TopCenterGlobal.Y < leaderY - 16.0f)
                {
                    continue;
                }

                float d = Mathf.Abs(cand.TopCenterGlobal.Y - midY);
                if (d < bestDist)
                {
                    bestDist = d;
                    closest = cand;
                }
            }
        }

        if (closest.HasValue)
        {
            return closest.Value.TopCenterGlobal + new Vector2((slotIndex - 1.5f) * 8f, 0f);
        }

        return new Vector2(320f, camera.GlobalPosition.Y);
    }
}
