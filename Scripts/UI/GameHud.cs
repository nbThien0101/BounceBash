using System;
using System.Collections.Generic;
using System.Linq;
using BounceBash.Core;
using BounceBash.Gameplay;
using BounceBash.Level;
using BounceBash.Net;
using BounceBash.Player;
using Godot;

namespace BounceBash.UI;

/// <summary>
/// In-game HUD & Match Result Overlay (GDD Section 1.5.5, 1.6.3, Table 1.2-01, and Tests T08-T09):
/// - Displays P1-P4 status cards along top edge (never blocking character feet or platform edges)
/// - Shows remaining lives (3 lives), charge level, personal GoalZone timer, Quick Match timer,
///   and the exact icon for Kinetic Nullifier (Buff) or Direction Lock (Left/Straight/Right Debuff).
/// - Displays Victory / Match Over screen with:
///   * "CHƠI LẠI (GIỮ MAP)": Replay with the same map seed
///   * "TẠO MAP MỚI (REGENERATE)": Replay with a freshly regenerated procedural map seed
///   * "VỀ MENU CHÍNH": Return to main menu
/// - Shows elimination announcements when a player is knocked out while other survivors continue
/// </summary>
public partial class GameHud : CanvasLayer
{
    public event Action? RetrySameMapRequested;
    public event Action? RetryRegenerateMapRequested;
    public event Action? ReturnToMenuRequested;

    private Control _hudRoot = null!;
    private Control _resultOverlay = null!;
    private Label _resultTitleLabel = null!;
    private Label _resultDetailLabel = null!;
    private Button _btnRetrySame = null!;
    private Button _btnRetryRegen = null!;
    private Button _btnMenu = null!;

    private IReadOnlyList<PlayerController> _players = [];
    private GoalZone? _goalZone;
    private ItemSystem? _itemSystem;
    private MapAssembler? _mapAssembler;
    private float _quickMatchRemaining;

    private string _eliminationNotice = "";
    private float _eliminationNoticeTimer;

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
            Color = new Color(0.04f, 0.06f, 0.10f, 0.88f),
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };
        _resultOverlay.AddChild(dimmer);

        var card = new ColorRect
        {
            Color = new Color("142232"),
            Position = new Vector2(110f, 65f),
            Size = new Vector2(420f, 230f)
        };
        _resultOverlay.AddChild(card);

        _resultTitleLabel = new Label
        {
            Text = "VICTORY!",
            Position = new Vector2(120f, 78f),
            Size = new Vector2(400f, 30f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("ffd166")
        };
        _resultTitleLabel.AddThemeFontSizeOverride("font_size", 17);
        _resultOverlay.AddChild(_resultTitleLabel);

        _resultDetailLabel = new Label
        {
            Text = "",
            Position = new Vector2(120f, 114f),
            Size = new Vector2(400f, 75f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("e9ecef")
        };
        _resultDetailLabel.AddThemeFontSizeOverride("font_size", 10);
        _resultOverlay.AddChild(_resultDetailLabel);

        // Option 1: Replay with same map
        _btnRetrySame = new Button
        {
            Text = "CHƠI LẠI (GIỮ MAP)",
            Position = new Vector2(125f, 198f),
            Size = new Vector2(185f, 35f),
            Modulate = new Color("ffd166")
        };
        _btnRetrySame.AddThemeFontSizeOverride("font_size", 10);
        _btnRetrySame.Pressed += () => RetrySameMapRequested?.Invoke();
        _resultOverlay.AddChild(_btnRetrySame);

        // Option 2: Replay with freshly regenerated map
        _btnRetryRegen = new Button
        {
            Text = "TẠO MAP MỚI (REGEN)",
            Position = new Vector2(320f, 198f),
            Size = new Vector2(195f, 35f),
            Modulate = new Color("56cfe1")
        };
        _btnRetryRegen.AddThemeFontSizeOverride("font_size", 10);
        _btnRetryRegen.Pressed += () => RetryRegenerateMapRequested?.Invoke();
        _resultOverlay.AddChild(_btnRetryRegen);

        // Option 3: Return to Menu
        _btnMenu = new Button
        {
            Text = "← VỀ MENU CHÍNH",
            Position = new Vector2(230f, 244f),
            Size = new Vector2(180f, 32f),
            Modulate = new Color("e0fbfc")
        };
        _btnMenu.AddThemeFontSizeOverride("font_size", 10);
        _btnMenu.Pressed += () => ReturnToMenuRequested?.Invoke();
        _resultOverlay.AddChild(_btnMenu);
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
        _eliminationNotice = "";
        _eliminationNoticeTimer = 0f;
        _resultOverlay.Visible = false;
        _hudRoot.QueueRedraw();
    }

    public void RefreshHud(float quickMatchRemaining, float dt = 0f)
    {
        _quickMatchRemaining = quickMatchRemaining;
        if (_eliminationNoticeTimer > 0f)
        {
            _eliminationNoticeTimer = Mathf.Max(0f, _eliminationNoticeTimer - dt);
        }
        _hudRoot.QueueRedraw();
    }

    public void ShowEliminationNotice(string notice)
    {
        _eliminationNotice = notice;
        _eliminationNoticeTimer = 4.0f;
        _hudRoot.QueueRedraw();
    }

    public void ShowMatchResult(string title, string detail, Color titleColor)
    {
        _resultTitleLabel.Text = title;
        _resultTitleLabel.Modulate = titleColor;
        _resultDetailLabel.Text = detail;

        bool isLanClient = NetworkManager.Instance != null && NetworkManager.Instance.IsLanActive && !NetworkManager.Instance.IsHost;
        if (isLanClient)
        {
            _btnRetrySame.Disabled = true;
            _btnRetrySame.Text = "CHỜ CHỦ PHÒNG BẮT ĐẦU...";
            _btnRetryRegen.Disabled = true;
            _btnRetryRegen.Text = "CHỜ CHỦ PHÒNG BẮT ĐẦU...";
        }
        else
        {
            _btnRetrySame.Disabled = false;
            _btnRetrySame.Text = "CHƠI LẠI (GIỮ MAP)";
            _btnRetryRegen.Disabled = false;
            _btnRetryRegen.Text = "TẠO MAP MỚI (REGEN)";
        }

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

            int localPeerId = NetworkManager.Instance.IsLanActive ? Multiplayer.GetUniqueId() : 1;
            PlayerController? localPlayer = null;

            // Draw compact P1..P4 HUD cards at top-left (height 34px so it never blocks gameplay)
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.PeerId == localPeerId)
                {
                    localPlayer = p;
                }

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
                subLine = "[R] Đổi map  |  [ESC] Menu";
            }
            DrawString(ThemeDB.FallbackFont, new Vector2(482f, 31f), subLine, HorizontalAlignment.Left, -1f, 8, new Color("ffd166"));

            // Elimination Announcement banner (e.g. "P3 ĐÃ BỊ LOẠI! CÒN 2 NGƯỜI CHƠI")
            if (_owner._eliminationNoticeTimer > 0f && !string.IsNullOrEmpty(_owner._eliminationNotice))
            {
                Rect2 noticeRect = new(170f, 44f, 300f, 22f);
                DrawRect(noticeRect, new Color(0.08f, 0.12f, 0.18f, 0.92f), true);
                DrawRect(noticeRect, new Color("ff595e"), false, 1.0f);
                DrawString(ThemeDB.FallbackFont, new Vector2(175f, 59f), _owner._eliminationNotice, HorizontalAlignment.Center, 290f, 9, new Color("ffd166"));
            }

            // Spectator mode banner for local player if they were eliminated but game is still running
            if (localPlayer != null && localPlayer.IsEliminated && players.Count(x => !x.IsEliminated) > 1)
            {
                Rect2 specRect = new(150f, 332f, 340f, 22f);
                DrawRect(specRect, new Color(0.06f, 0.08f, 0.12f, 0.88f), true);
                DrawRect(specRect, new Color("adb5bd"), false, 1.0f);
                DrawString(ThemeDB.FallbackFont, new Vector2(155f, 347f), "BẠN ĐÃ BỊ LOẠI (HẾT 3 LƯỢT) — ĐANG THEO DÕI TRẬN ĐẤU...", HorizontalAlignment.Center, 330f, 8, new Color("e9ecef"));
            }
        }
    }
}
