using System;
using System.Collections.Generic;
using BounceBash.Core;
using BounceBash.Gameplay;
using BounceBash.Level;
using BounceBash.Player;
using Godot;

namespace BounceBash.UI;

/// <summary>
/// In-game HUD & Match Result Overlay (GDD Section 1.5.5, 1.6.3, Table 1.2-01, and Tests T08-T09):
/// - Displays P1-P4 status cards along top edge (never blocking character feet or platform edges)
/// - Shows remaining lives (3 lives), charge level, personal GoalZone timer, Quick Match timer,
///   and the exact icon for Kinetic Nullifier (Buff) or Direction Lock (Left/Straight/Right Debuff).
/// - Displays Victory / Match Over screen with Retry and Return to Menu buttons.
/// </summary>
public partial class GameHud : CanvasLayer
{
    public event Action? RetryMatchRequested;
    public event Action? ReturnToMenuRequested;

    private Control _hudRoot = null!;
    private Control _resultOverlay = null!;
    private Label _resultTitleLabel = null!;
    private Label _resultDetailLabel = null!;

    private IReadOnlyList<PlayerController> _players = [];
    private GoalZone? _goalZone;
    private ItemSystem? _itemSystem;
    private MapAssembler? _mapAssembler;
    private float _quickMatchRemaining;

    public override void _Ready()
    {
        Layer = 5;

        _hudRoot = new TopHudCanvas(this)
        {
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_hudRoot);

        BuildResultOverlay();
    }

    private void BuildResultOverlay()
    {
        _resultOverlay = new Control
        {
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            Visible = false
        };
        AddChild(_resultOverlay);

        var dimmer = new ColorRect
        {
            Color = new Color(0.04f, 0.06f, 0.10f, 0.85f),
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };
        _resultOverlay.AddChild(dimmer);

        var card = new ColorRect
        {
            Color = new Color("152232"),
            Position = new Vector2(140f, 75f),
            Size = new Vector2(360f, 210f)
        };
        _resultOverlay.AddChild(card);

        _resultTitleLabel = new Label
        {
            Text = "VICTORY!",
            Position = new Vector2(160f, 92f),
            Size = new Vector2(320f, 32f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("ffd166")
        };
        _resultTitleLabel.AddThemeFontSizeOverride("font_size", 18);
        _resultOverlay.AddChild(_resultTitleLabel);

        _resultDetailLabel = new Label
        {
            Text = "",
            Position = new Vector2(160f, 132f),
            Size = new Vector2(320f, 75f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("e9ecef")
        };
        _resultDetailLabel.AddThemeFontSizeOverride("font_size", 10);
        _resultOverlay.AddChild(_resultDetailLabel);

        var btnRetry = new Button
        {
            Text = "CHƠI LẠI (RETRY)",
            Position = new Vector2(165f, 224f),
            Size = new Vector2(145f, 36f),
            Modulate = new Color("ffd166")
        };
        btnRetry.AddThemeFontSizeOverride("font_size", 10);
        btnRetry.Pressed += () => RetryMatchRequested?.Invoke();
        _resultOverlay.AddChild(btnRetry);

        var btnMenu = new Button
        {
            Text = "VỀ MENU CHÍNH",
            Position = new Vector2(325f, 224f),
            Size = new Vector2(145f, 36f),
            Modulate = new Color("a8dadc")
        };
        btnMenu.AddThemeFontSizeOverride("font_size", 10);
        btnMenu.Pressed += () => ReturnToMenuRequested?.Invoke();
        _resultOverlay.AddChild(btnMenu);
    }

    public void BindMatchState(
        IReadOnlyList<PlayerController> players,
        GoalZone goalZone,
        ItemSystem itemSystem,
        MapAssembler mapAssembler,
        float quickMatchRemaining)
    {
        _players = players;
        _goalZone = goalZone;
        _itemSystem = itemSystem;
        _mapAssembler = mapAssembler;
        _quickMatchRemaining = quickMatchRemaining;
        _resultOverlay.Visible = false;
        _hudRoot.QueueRedraw();
    }

    public void RefreshHud(float quickMatchRemaining)
    {
        _quickMatchRemaining = quickMatchRemaining;
        _hudRoot.QueueRedraw();
    }

    public void ShowMatchResult(string title, string detail, Color titleColor)
    {
        _resultTitleLabel.Text = title;
        _resultTitleLabel.Modulate = titleColor;
        _resultDetailLabel.Text = detail;
        _resultOverlay.Visible = true;
    }

    public void HideMatchResult()
    {
        _resultOverlay.Visible = false;
    }

    private partial class TopHudCanvas : Control
    {
        private readonly GameHud _owner;

        public TopHudCanvas(GameHud owner)
        {
            _owner = owner;
        }

        public override void _Draw()
        {
            var players = _owner._players;
            if (players == null || players.Count == 0 || ThemeDB.FallbackFont == null)
            {
                return;
            }

            // Draw compact P1..P4 HUD cards at top-left (height 34px so it never blocks gameplay)
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                float x0 = 8f + i * 116f;
                float y0 = 6f;
                Rect2 cardRect = new(x0, y0, 112f, 34f);

                DrawRect(cardRect, new Color(0.05f, 0.08f, 0.13f, 0.86f), true);
                DrawRect(cardRect, p.TagColor, false, 1.0f);

                // Player Tag & Lives (3 hearts or X if eliminated - GDD 1.5.5)
                string livesStr = p.IsEliminated
                    ? "X LOẠI"
                    : new string('♥', p.RemainingLives) + new string('·', Mathf.Max(0, 3 - p.RemainingLives));

                DrawString(
                    ThemeDB.FallbackFont,
                    new Vector2(x0 + 5f, y0 + 11f),
                    $"{p.PlayerTag} {livesStr}",
                    HorizontalAlignment.Left,
                    -1f,
                    9,
                    p.IsEliminated ? new Color("adb5bd") : p.TagColor);

                // Goal Timer (in PvP)
                if (GameSession.Instance.CurrentMode != GameMode.SinglePlayer)
                {
                    DrawString(
                        ThemeDB.FallbackFont,
                        new Vector2(x0 + 5f, y0 + 23f),
                        $"Đích: {p.PersonalGoalTimer:0.0}s",
                        HorizontalAlignment.Left,
                        -1f,
                        8,
                        new Color("f1faee"));
                }
                else
                {
                    int altitudeMeters = Mathf.Max(0, Mathf.RoundToInt(-p.GlobalPosition.Y / 16f));
                    DrawString(
                        ThemeDB.FallbackFont,
                        new Vector2(x0 + 5f, y0 + 23f),
                        $"Độ cao: {altitudeMeters}m",
                        HorizontalAlignment.Left,
                        -1f,
                        8,
                        new Color("f1faee"));
                }

                // Charge meter bar at bottom of card
                DrawRect(new Rect2(x0 + 5f, y0 + 27f, 68f, 4f), new Color(0.15f, 0.18f, 0.22f, 1f), true);
                if (p.ChargeRatio > 0f)
                {
                    DrawRect(new Rect2(x0 + 5f, y0 + 27f, 68f * p.ChargeRatio, 4f), new Color("ffca3a"), true);
                }

                // Active Buff / Debuff Icon (GDD Table 1.2-01)
                if (_owner._itemSystem != null)
                {
                    if (p.HasKineticNullifier)
                    {
                        var icon = _owner._itemSystem.GetKineticNullifierIcon();
                        if (icon != null)
                        {
                            DrawTextureRect(icon, new Rect2(x0 + 76f, y0 + 4f, 16f, 16f), false);
                        }
                    }

                    if (p.HasDirectionLock)
                    {
                        var icon = _owner._itemSystem.GetDirectionLockIcon(p.LockedJumpDirection);
                        if (icon != null)
                        {
                            DrawTextureRect(icon, new Rect2(x0 + 93f, y0 + 4f, 16f, 16f), false);
                        }
                    }
                }
            }

            // Right-side Match Status badge (Combination order, GoalZone state, QuickMatch timer, ESC hint)
            Rect2 rightRect = new(476f, 6f, 156f, 34f);
            DrawRect(rightRect, new Color(0.05f, 0.08f, 0.13f, 0.86f), true);

            string comboLabel = _owner._mapAssembler?.ActiveCombination == MapCombination.CombinationA_JungleThenRock
                ? "Tổ hợp A: Rừng→Núi→Đền"
                : "Tổ hợp B: Núi→Rừng→Đền";
            DrawString(ThemeDB.FallbackFont, new Vector2(482f, 17f), comboLabel, HorizontalAlignment.Left, -1f, 8, new Color("8ecae6"));

            string subLine;
            if (GameSession.Instance.CurrentMode == GameMode.PvPQuickMatch)
            {
                subLine = $"Giờ còn: {_owner._quickMatchRemaining:0.0}s | [ESC] Menu";
            }
            else if (_owner._goalZone != null && _owner._goalZone.IsContested)
            {
                subLine = "ĐÍCH: ĐANG TRANH CHẤP!";
            }
            else
            {
                subLine = "[R] Chơi lại  |  [ESC] Menu";
            }
            DrawString(ThemeDB.FallbackFont, new Vector2(482f, 31f), subLine, HorizontalAlignment.Left, -1f, 8, new Color("ffd166"));
        }
    }
}
