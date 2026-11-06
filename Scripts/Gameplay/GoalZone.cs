using System.Collections.Generic;
using BounceBash.Config;
using BounceBash.Core;
using BounceBash.Player;
using Godot;

namespace BounceBash.Gameplay;

/// <summary>
/// GoalZone & Match Win Condition evaluator (GDD Section 1.2.5, 1.4.4, and Test T09):
/// - Singleplayer: Touching the Elixir on the Temple pedestal wins immediately (no contested timer).
/// - PvP Default (King of the Hill):
///   * Exactly 1 active player inside GoalZone: their personal timer counts down toward 0.
///   * 2+ active players inside GoalZone: CONTESTED! All timers pause and preserve progress.
///   * Leaving GoalZone: resets that player's personal timer back to GoalHoldTimeSeconds.
///   * First player whose personal timer reaches 0 wins.
/// </summary>
public partial class GoalZone : Node2D
{
    public Rect2 ZoneBoundsGlobal { get; private set; }
    public bool IsContested { get; private set; }
    public PlayerController? CurrentHolder { get; private set; }
    public PlayerController? Winner { get; private set; }

    private Texture2D? _elixirTexture;
    private TuningConfig Tuning => GameSession.Instance.Tuning;

    public override void _Ready()
    {
        if (ResourceLoader.Exists("res://Assets/Sprites/Props/elixir.png"))
        {
            _elixirTexture = GD.Load<Texture2D>("res://Assets/Sprites/Props/elixir.png");
        }
    }

    public void ConfigureGoalPedestal(Vector2 pedestalTopCenterGlobal)
    {
        GlobalPosition = pedestalTopCenterGlobal;
        // Wide enough for King-of-the-Hill contesting, open on sides for knock-offs (GDD 1.4.4)
        ZoneBoundsGlobal = new Rect2(
            pedestalTopCenterGlobal.X - 75.0f,
            pedestalTopCenterGlobal.Y - 56.0f,
            150.0f,
            58.0f);
        IsContested = false;
        CurrentHolder = null;
        Winner = null;
        QueueRedraw();
    }

    public void EvaluateGoalZone(float dt, IReadOnlyList<PlayerController> players, GameMode mode)
    {
        if (Winner != null)
        {
            return;
        }

        var occupants = new List<PlayerController>();

        foreach (var player in players)
        {
            if (player.IsEliminated)
            {
                continue;
            }

            Vector2 center = player.GlobalPosition + new Vector2(0f, -12f);
            bool inside = ZoneBoundsGlobal.HasPoint(center);

            if (inside)
            {
                occupants.Add(player);
            }
            else
            {
                // GDD 1.2.5 & T09: Leaving the GoalZone resets that player's personal timer
                player.PersonalGoalTimer = Tuning.GoalHoldTimeSeconds;
            }
        }

        if (mode == GameMode.SinglePlayer)
        {
            if (occupants.Count > 0)
            {
                Winner = occupants[0];
                GameSession.Instance.PlaySfx("sfx_victory", 1.0f, 0.9f);
            }
            return;
        }

        // PvP King of the Hill rules (GDD 1.2.5 & T09)
        if (occupants.Count == 1)
        {
            IsContested = false;
            CurrentHolder = occupants[0];
            CurrentHolder.PersonalGoalTimer = Mathf.Max(0f, CurrentHolder.PersonalGoalTimer - dt);

            if (CurrentHolder.PersonalGoalTimer <= 0f)
            {
                Winner = CurrentHolder;
                GameSession.Instance.PlaySfx("sfx_victory", 1.0f, 0.9f);
            }
        }
        else if (occupants.Count >= 2)
        {
            // Contested: pause all active timers and preserve progress!
            IsContested = true;
            CurrentHolder = null;
        }
        else
        {
            IsContested = false;
            CurrentHolder = null;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 localZone = new(-75f, -56f, 150f, 56f);
        Color zoneCol = IsContested
            ? new Color(1.0f, 0.3f, 0.35f, 0.25f)
            : (CurrentHolder != null
                ? new Color(CurrentHolder.TagColor.R, CurrentHolder.TagColor.G, CurrentHolder.TagColor.B, 0.25f)
                : new Color(0.98f, 0.92f, 0.25f, 0.16f));

        DrawRect(localZone, zoneCol, true);
        DrawRect(localZone, zoneCol with { A = 0.85f }, false, 1.5f);

        // Draw the Elixir of Immortality floating above the pedestal center
        if (_elixirTexture != null)
        {
            DrawTextureRect(_elixirTexture, new Rect2(-12f, -34f, 24f, 24f), false);
        }

        if (ThemeDB.FallbackFont != null)
        {
            string statusText = IsContested
                ? "TRANH CHẤP! (TẠM DỪNG)"
                : (CurrentHolder != null
                    ? $"{CurrentHolder.PlayerTag} GIỮ ĐÍCH: {CurrentHolder.PersonalGoalTimer:0.0}s"
                    : "VÙNG TRANH BẢO VẬT");

            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(-68f, -62f),
                statusText,
                HorizontalAlignment.Center,
                136f,
                9,
                 new Color("fbf236"));
        }
    }
}
