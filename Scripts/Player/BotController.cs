using System;
using System.Collections.Generic;
using BounceBash.Config;
using BounceBash.Core;
using BounceBash.Level;
using Godot;

namespace BounceBash.Player;

/// <summary>
/// PvE Bot AI controller implementing GDD v0.9.5 Section 1.2.6, 1.7.2, and Slide 22:
/// - Waypoint-based vertical platform navigation using MapAssembler platform anchors
/// - Evaluates jump reachability, angles, and cyclic charge timing
/// - Generates valid input signals (NetMoveInput, NetJumpHeld, NetJumpReleasedBuffer)
/// - Engages in GoalZone King of the Hill (holds pedestal when alone, contests human player)
/// - Operates locally in PvE without fake LAN clients or network RPC overhead
/// </summary>
public class BotController
{
    private enum BotState
    {
        IdleDeciding,
        WalkingToEdge,
        ChargingJump,
        AirborneWaiting,
        ContestingGoal
    }

    private BotState _currentState = BotState.IdleDeciding;
    private float _decisionTimer;
    private float _chargingTimer;
    private float _targetChargeRatio;
    private int _targetJumpDirection;
    private Vector2 _targetPlatformPos;

    private readonly Random _rng = new();

    public void Reset()
    {
        _currentState = BotState.IdleDeciding;
        _decisionTimer = 0.2f;
        _chargingTimer = 0f;
        _targetChargeRatio = 0f;
        _targetJumpDirection = 0;
    }

    public void UpdateBotInput(
        PlayerController bot,
        float dt,
        IReadOnlyList<PlatformCandidate> platforms,
        Vector2 goalZonePos,
        PlayerController? humanOpponent)
    {
        if (bot.IsEliminated)
        {
            bot.NetMoveInput = 0f;
            bot.NetJumpHeld = false;
            bot.NetJumpReleasedBuffer = false;
            return;
        }

        // Airborne: No air control allowed in Bounce Bash (GDD 1.2.1)
        if (bot.CurrentState == PlayerState.Airborne)
        {
            _currentState = BotState.AirborneWaiting;
            bot.NetMoveInput = 0f;
            bot.NetJumpHeld = false;
            bot.NetJumpReleasedBuffer = false;
            return;
        }

        // Landing recovery: wait for landing stun to complete
        if (bot.CurrentState == PlayerState.Landing)
        {
            bot.NetMoveInput = 0f;
            bot.NetJumpHeld = false;
            bot.NetJumpReleasedBuffer = false;
            return;
        }

        var tuning = GameSession.Instance.Tuning;

        // If inside GoalZone (King of the Hill)
        float distToGoal = bot.GlobalPosition.DistanceTo(goalZonePos);
        if (distToGoal < 80f)
        {
            HandleGoalZoneBehavior(bot, dt, goalZonePos, humanOpponent);
            return;
        }

        switch (_currentState)
        {
            case BotState.AirborneWaiting:
                // Just landed
                _currentState = BotState.IdleDeciding;
                _decisionTimer = (float)(_rng.NextDouble() * 0.15 + 0.15); // human-like reaction time (0.15 - 0.30s)
                bot.NetMoveInput = 0f;
                bot.NetJumpHeld = false;
                break;

            case BotState.IdleDeciding:
                bot.NetMoveInput = 0f;
                bot.NetJumpHeld = false;
                _decisionTimer -= dt;
                if (_decisionTimer <= 0f)
                {
                    PlanNextJump(bot, platforms, goalZonePos, tuning);
                }
                break;

            case BotState.WalkingToEdge:
                _decisionTimer -= dt;
                float walkDir = Mathf.Sign(_targetPlatformPos.X - bot.GlobalPosition.X);
                bot.NetMoveInput = walkDir;

                // If within reasonable vertical alignment or time up, start charging
                if (Mathf.Abs(_targetPlatformPos.X - bot.GlobalPosition.X) < 130f || _decisionTimer <= 0f)
                {
                    StartCharging(bot, tuning);
                }
                break;

            case BotState.ChargingJump:
                _chargingTimer += dt;
                bot.NetMoveInput = _targetJumpDirection;
                bot.NetJumpHeld = true;

                // GDD v0.9.5 Cyclic charge: check if current charge ratio is close to desired target ratio
                bool releaseNow = false;
                if (bot.ChargeRatio >= _targetChargeRatio)
                {
                    releaseNow = true;
                }
                // Watchdog to prevent infinite charging
                else if (_chargingTimer >= tuning.MaxChargeTime * 2.2f)
                {
                    releaseNow = true;
                }

                if (releaseNow)
                {
                    bot.NetJumpHeld = false;
                    bot.NetJumpReleasedBuffer = true;
                    _currentState = BotState.AirborneWaiting;
                }
                break;
        }
    }

    private void PlanNextJump(
        PlayerController bot,
        IReadOnlyList<PlatformCandidate> platforms,
        Vector2 goalZonePos,
        TuningConfig tuning)
    {
        // 1. Check if GoalZone is directly reachable
        float dyToGoal = goalZonePos.Y - bot.GlobalPosition.Y;
        float dxToGoal = goalZonePos.X - bot.GlobalPosition.X;
        if (dyToGoal < -20f && dyToGoal > -145f && Mathf.Abs(dxToGoal) < 180f)
        {
            _targetPlatformPos = goalZonePos;
            CalculateJumpParameters(bot, _targetPlatformPos, tuning);
            StartCharging(bot, tuning);
            return;
        }

        // 2. Search for valid upward platform candidates
        PlatformCandidate? bestCandidate = null;
        float bestScore = float.MinValue;

        foreach (var p in platforms)
        {
            float dy = p.TopCenterGlobal.Y - bot.GlobalPosition.Y;
            float dx = p.TopCenterGlobal.X - bot.GlobalPosition.X;

            // Must be higher than current position and within vertical jump reach (~145px)
            if (dy < -18f && dy > -150f)
            {
                // Score based on altitude gain, reasonable horizontal distance, and proximity to Goal
                float horizontalDist = Mathf.Abs(dx);
                if (horizontalDist <= 220f)
                {
                    float score = -dy * 1.5f - horizontalDist * 0.5f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCandidate = p;
                    }
                }
            }
        }

        if (bestCandidate.HasValue)
        {
            _targetPlatformPos = bestCandidate.Value.TopCenterGlobal;
            float dx = _targetPlatformPos.X - bot.GlobalPosition.X;

            // If target platform is slightly far horizontally, walk towards it first
            if (Mathf.Abs(dx) > 140f)
            {
                _currentState = BotState.WalkingToEdge;
                _decisionTimer = 0.35f;
            }
            else
            {
                CalculateJumpParameters(bot, _targetPlatformPos, tuning);
                StartCharging(bot, tuning);
            }
        }
        else
        {
            // Fallback: no direct platform found above; do a standard jump towards center/upward
            _targetPlatformPos = new Vector2(320f, bot.GlobalPosition.Y - 80f);
            CalculateJumpParameters(bot, _targetPlatformPos, tuning);
            StartCharging(bot, tuning);
        }
    }

    private void CalculateJumpParameters(PlayerController bot, Vector2 target, TuningConfig tuning)
    {
        float dx = target.X - bot.GlobalPosition.X;
        float dy = target.Y - bot.GlobalPosition.Y;

        // Choose jump direction (-1, 0, +1)
        int dir;
        if (dx > 25f)
        {
            dir = 1;
        }
        else if (dx < -25f)
        {
            dir = -1;
        }
        else
        {
            dir = 0;
        }

        // Handle Direction Lock debuff fallback (GDD 1.2.3 & T08)
        if (bot.HasDirectionLock && dir == bot.LockedJumpDirection)
        {
            dir = bot.LockedJumpDirection == 0 ? bot.FacingDirection : 0;
        }

        _targetJumpDirection = dir;

        // Required vertical velocity: v^2 = 2 * g * h
        float targetHeight = Mathf.Max(20f, -dy + 22f);
        float vyReq = Mathf.Sqrt(2f * tuning.Gravity * targetHeight);
        float ratio = (vyReq - tuning.MinJumpSpeed) / (tuning.MaxJumpSpeed - tuning.MinJumpSpeed);
        _targetChargeRatio = Mathf.Clamp(ratio, 0.25f, 0.95f);
    }

    private void StartCharging(PlayerController bot, TuningConfig tuning)
    {
        _currentState = BotState.ChargingJump;
        _chargingTimer = 0f;
        bot.NetMoveInput = _targetJumpDirection;
        bot.NetJumpHeld = true;
        bot.NetJumpReleasedBuffer = false;
    }

    private void HandleGoalZoneBehavior(
        PlayerController bot,
        float dt,
        Vector2 goalZonePos,
        PlayerController? humanOpponent)
    {
        // If human is also near or in GoalZone: contest by pushing/hopping toward them
        if (humanOpponent != null && !humanOpponent.IsEliminated && humanOpponent.GlobalPosition.DistanceTo(goalZonePos) < 90f)
        {
            _currentState = BotState.ContestingGoal;
            float dx = humanOpponent.GlobalPosition.X - bot.GlobalPosition.X;
            bot.NetMoveInput = Mathf.Sign(dx);

            // Occasional hop to push opponent
            _decisionTimer -= dt;
            if (_decisionTimer <= 0f && bot.CurrentState == PlayerState.GroundedIdle)
            {
                _decisionTimer = 1.0f;
                _targetJumpDirection = Mathf.Sign(dx) > 0 ? 1 : -1;
                _targetChargeRatio = 0.35f;
                StartCharging(bot, GameSession.Instance.Tuning);
            }
        }
        else
        {
            // Camp on the pedestal to hold GoalZone and decrement win timer
            _currentState = BotState.ContestingGoal;
            float diffX = goalZonePos.X - bot.GlobalPosition.X;
            if (Mathf.Abs(diffX) > 15f)
            {
                bot.NetMoveInput = Mathf.Sign(diffX) * 0.5f;
            }
            else
            {
                bot.NetMoveInput = 0f;
            }
            bot.NetJumpHeld = false;
            bot.NetJumpReleasedBuffer = false;
        }
    }
}
