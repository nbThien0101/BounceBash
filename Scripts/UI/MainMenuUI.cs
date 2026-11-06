using System;
using BounceBash.Core;
using BounceBash.Net;
using Godot;

namespace BounceBash.UI;

/// <summary>
/// Full Menu Navigation UI matching GDD Section 1.6.3 Wireframes (Figures 1.6-02 to 1.6-06):
/// - 01 / MENU CHÍNH (Một Người, Nhiều Người, Thử 2 Người Local, Cài Đặt)
/// - 02 / NHIỀU NGƯỜI (Tạo Phòng, Vào Phòng, Quay Lại)
/// - 03 / PHÒNG LAN (P1-P4 Lobby slots, Character Male/Female toggle, Map Combination, IP/Port info, Bắt Đầu)
/// - 04 / VÀO PHÒNG (LAN IP input + Optional Port dialog)
/// - 05 / CÀI ĐẶT (Nhạc nền, Hiệu ứng, Độ sáng, Quỹ đạo nhảy, Điều khiển)
/// </summary>
public partial class MainMenuUI : CanvasLayer
{
    public event Action? StartSinglePlayerRequested;
    public event Action? StartLocalTwoPlayerRequested;
    public event Action? HostStartLanMatchRequested;

    private Control _root = null!;
    private Control _panelMain = null!;
    private Control _panelMultiplayerChoice = null!;
    private Control _panelLanLobby = null!;
    private Control _panelJoinDialog = null!;
    private Control _panelSettings = null!;

    // Lobby UI elements
    private readonly Label[] _slotLabels = new Label[4];
    private Label _lobbyInfoLabel = null!;
    private Button _btnStartLanMatch = null!;
    private Button _btnComboToggle = null!;
    private Button _btnModeToggle = null!;
    private LineEdit _ipInput = null!;
    private LineEdit _portInput = null!;

    public override void _Ready()
    {
        Layer = 10;
        BuildUiHierarchy();
        ShowScreen(_panelMain);

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.LobbyUpdated += RefreshLobbyUi;
        }
    }

    public override void _ExitTree()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.LobbyUpdated -= RefreshLobbyUi;
        }
    }

    private void ShowScreen(Control targetPanel)
    {
        _panelMain.Visible = targetPanel == _panelMain;
        _panelMultiplayerChoice.Visible = targetPanel == _panelMultiplayerChoice;
        _panelLanLobby.Visible = targetPanel == _panelLanLobby;
        _panelJoinDialog.Visible = targetPanel == _panelJoinDialog;
        _panelSettings.Visible = targetPanel == _panelSettings;
        RefreshLobbyUi();
    }

    private void BuildUiHierarchy()
    {
        _root = new Control
        {
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };
        AddChild(_root);

        // Night sky & Forbidden Temple silhouette backdrop (matching GDD Figures 1.6-02..06)
        var bg = new ColorRect
        {
            Color = new Color("0f1926"),
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };
        _root.AddChild(bg);

        // Moon & Temple Silhouette on Right side
        var moon = new ColorRect
        {
            Color = new Color("f7d06b"),
            Position = new Vector2(465f, 32f),
            Size = new Vector2(56f, 56f)
        };
        _root.AddChild(moon);

        var templeRoof = new ColorRect
        {
            Color = new Color("253447"),
            Position = new Vector2(415f, 110f),
            Size = new Vector2(150f, 24f)
        };
        _root.AddChild(templeRoof);

        var templeBody = new ColorRect
        {
            Color = new Color("1b2737"),
            Position = new Vector2(430f, 134f),
            Size = new Vector2(120f, 226f)
        };
        _root.AddChild(templeBody);

        // Header Title
        var titleLbl = new Label
        {
            Text = "BOUNCE BASH",
            Position = new Vector2(42f, 38f),
            Modulate = new Color("f8f9fa")
        };
        titleLbl.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(titleLbl);

        var subTitleLbl = new Label
        {
            Text = "THE ADVENTURE TO THE FORBIDDEN TEMPLE  |  GODOT 4 C#",
            Position = new Vector2(42f, 66f),
            Modulate = new Color("8ecae6")
        };
        subTitleLbl.AddThemeFontSizeOverride("font_size", 9);
        _root.AddChild(subTitleLbl);

        _panelMain = BuildMainPanel();
        _panelMultiplayerChoice = BuildMultiplayerChoicePanel();
        _panelLanLobby = BuildLanLobbyPanel();
        _panelJoinDialog = BuildJoinDialogPanel();
        _panelSettings = BuildSettingsPanel();

        _root.AddChild(_panelMain);
        _root.AddChild(_panelMultiplayerChoice);
        _root.AddChild(_panelLanLobby);
        _root.AddChild(_panelJoinDialog);
        _root.AddChild(_panelSettings);
    }

    private Control BuildMainPanel()
    {
        var panel = new Control { AnchorsPreset = (int)Control.LayoutPreset.FullRect };
        AddBadge(panel, "01 / MENU CHÍNH");

        var btnSingle = CreateMenuButton(panel, "MỘT NGƯỜI  (Vượt đền một mình - Auto-scroll)", new Vector2(42f, 102f), new Vector2(310f, 38f), true);
        btnSingle.Pressed += () => StartSinglePlayerRequested?.Invoke();

        var btnMulti = CreateMenuButton(panel, "NHIỀU NGƯỜI  (Chơi qua mạng nội bộ LAN 2-4 máy)", new Vector2(42f, 148f), new Vector2(310f, 38f), false);
        btnMulti.Pressed += () => ShowScreen(_panelMultiplayerChoice);

        var btnLocal2P = CreateMenuButton(panel, "THỬ 2 NGƯỜI TRÊN 1 MÁY  (P1: A/D/Space | P2: J/L/I)", new Vector2(42f, 194f), new Vector2(310f, 38f), false);
        btnLocal2P.Pressed += () => StartLocalTwoPlayerRequested?.Invoke();

        var btnSettings = CreateMenuButton(panel, "CÀI ĐẶT  (Âm thanh, hiển thị, điều khiển)", new Vector2(42f, 240f), new Vector2(310f, 38f), false);
        btnSettings.Pressed += () => ShowScreen(_panelSettings);

        var hint = new Label
        {
            Text = "Điều khiển: A/D di chuyển | Giữ SPACE/W tích lực + A/D chọn hướng -> Nhả để nhảy",
            Position = new Vector2(42f, 325f),
            Modulate = new Color("adb5bd")
        };
        hint.AddThemeFontSizeOverride("font_size", 9);
        panel.AddChild(hint);

        return panel;
    }

    private Control BuildMultiplayerChoicePanel()
    {
        var panel = new Control { AnchorsPreset = (int)Control.LayoutPreset.FullRect };
        AddBadge(panel, "02 / NHIỀU NGƯỜI");

        var btnHost = CreateMenuButton(panel, "TẠO PHÒNG  (Trở thành chủ phòng LAN - Chiếm P1)", new Vector2(42f, 120f), new Vector2(310f, 44f), true);
        btnHost.Pressed += () =>
        {
            NetworkManager.Instance.HostLanRoom(NetworkManager.DefaultLanPort);
            ShowScreen(_panelLanLobby);
        };

        var btnJoin = CreateMenuButton(panel, "VÀO PHÒNG  (Kết nối địa chỉ IP mạng nội bộ)", new Vector2(42f, 178f), new Vector2(310f, 44f), false);
        btnJoin.Pressed += () => ShowScreen(_panelJoinDialog);

        var btnBack = CreateMenuButton(panel, "← QUAY LẠI MENU CHÍNH", new Vector2(42f, 260f), new Vector2(200f, 32f), false);
        btnBack.Pressed += () => ShowScreen(_panelMain);

        return panel;
    }

    private Control BuildLanLobbyPanel()
    {
        var panel = new Control { AnchorsPreset = (int)Control.LayoutPreset.FullRect };
        AddBadge(panel, "03 / PHÒNG LAN");

        for (int i = 0; i < 4; i++)
        {
            var rowBg = new ColorRect
            {
                Color = new Color("162231"),
                Position = new Vector2(42f, 95f + i * 38f),
                Size = new Vector2(330f, 32f)
            };
            panel.AddChild(rowBg);

            var colorTag = new ColorRect
            {
                Color = GameSession.PlayerPalette[i],
                Position = new Vector2(46f, 99f + i * 38f),
                Size = new Vector2(28f, 24f)
            };
            panel.AddChild(colorTag);

            var pTag = new Label
            {
                Text = $"P{i + 1}",
                Position = new Vector2(52f, 103f + i * 38f),
                Modulate = new Color("0b0f19")
            };
            pTag.AddThemeFontSizeOverride("font_size", 10);
            panel.AddChild(pTag);

            var slotLbl = new Label
            {
                Text = i == 0 ? "CHỦ PHÒNG (Nam)" : "CHỜ NGƯỜI CHƠI...",
                Position = new Vector2(84f, 103f + i * 38f),
                Modulate = new Color("e9ecef")
            };
            slotLbl.AddThemeFontSizeOverride("font_size", 10);
            panel.AddChild(slotLbl);
            _slotLabels[i] = slotLbl;
        }

        _lobbyInfoLabel = new Label
        {
            Text = "THÔNG TIN PHÒNG LAN",
            Position = new Vector2(390f, 95f),
            Size = new Vector2(220f, 95f),
            Modulate = new Color("a8dadc")
        };
        _lobbyInfoLabel.AddThemeFontSizeOverride("font_size", 9);
        panel.AddChild(_lobbyInfoLabel);

        var btnGender = CreateMenuButton(panel, "ĐỔI NHÂN VẬT: NAM / NỮ", new Vector2(42f, 254f), new Vector2(160f, 30f), false);
        btnGender.Pressed += () => NetworkManager.Instance.RequestToggleLocalGender();

        _btnComboToggle = CreateMenuButton(panel, "MAP: NGẪU NHIÊN (A/B)", new Vector2(210f, 254f), new Vector2(162f, 30f), false);
        _btnComboToggle.Pressed += () =>
        {
            GameSession.Instance.SelectedCombination = GameSession.Instance.SelectedCombination switch
            {
                MapCombination.Random => MapCombination.CombinationA_JungleThenRock,
                MapCombination.CombinationA_JungleThenRock => MapCombination.CombinationB_RockThenJungle,
                _ => MapCombination.Random
            };
            RefreshLobbyUi();
        };

        _btnModeToggle = CreateMenuButton(panel, "CHẾ ĐỘ: PVP DEFAULT", new Vector2(390f, 200f), new Vector2(210f, 30f), false);
        _btnModeToggle.Pressed += () =>
        {
            GameSession.Instance.CurrentMode = GameSession.Instance.CurrentMode == GameMode.PvPQuickMatch
                ? GameMode.PvPDefault
                : GameMode.PvPQuickMatch;
            RefreshLobbyUi();
        };

        _btnStartLanMatch = CreateMenuButton(panel, "BẮT ĐẦU VÁN ĐẤU", new Vector2(390f, 240f), new Vector2(210f, 44f), true);
        _btnStartLanMatch.Pressed += () => HostStartLanMatchRequested?.Invoke();

        var btnLeave = CreateMenuButton(panel, "← RỜI PHÒNG", new Vector2(42f, 294f), new Vector2(160f, 28f), false);
        btnLeave.Pressed += () =>
        {
            NetworkManager.Instance.CloseLanSession();
            ShowScreen(_panelMultiplayerChoice);
        };

        return panel;
    }

    private Control BuildJoinDialogPanel()
    {
        var panel = new Control { AnchorsPreset = (int)Control.LayoutPreset.FullRect };
        AddBadge(panel, "04 / VÀO PHÒNG LAN");

        var box = new ColorRect
        {
            Color = new Color("142130"),
            Position = new Vector2(120f, 96f),
            Size = new Vector2(380f, 210f)
        };
        panel.AddChild(box);

        var lblIp = new Label
        {
            Text = "ĐỊA CHỈ IP MẠNG NỘI BỘ (BẮT BUỘC):",
            Position = new Vector2(140f, 112f),
            Modulate = new Color("a8dadc")
        };
        lblIp.AddThemeFontSizeOverride("font_size", 10);
        panel.AddChild(lblIp);

        _ipInput = new LineEdit
        {
            Text = "127.0.0.1",
            PlaceholderText = "VD: 192.168.1.10",
            Position = new Vector2(140f, 132f),
            Size = new Vector2(340f, 28f)
        };
        panel.AddChild(_ipInput);

        var lblPort = new Label
        {
            Text = "CỔNG (KHÔNG BẮT BUỘC - Mặc định 7777):",
            Position = new Vector2(140f, 172f),
            Modulate = new Color("a8dadc")
        };
        lblPort.AddThemeFontSizeOverride("font_size", 10);
        panel.AddChild(lblPort);

        _portInput = new LineEdit
        {
            Text = "",
            PlaceholderText = "Để trống nếu dùng cổng mặc định (7777)",
            Position = new Vector2(140f, 192f),
            Size = new Vector2(340f, 28f)
        };
        panel.AddChild(_portInput);

        var btnConnect = CreateMenuButton(panel, "KẾT NỐI", new Vector2(140f, 240f), new Vector2(160f, 38f), true);
        btnConnect.Pressed += () =>
        {
            int port = NetworkManager.DefaultLanPort;
            if (!string.IsNullOrWhiteSpace(_portInput.Text) && int.TryParse(_portInput.Text.Trim(), out int parsed))
            {
                port = parsed;
            }
            if (NetworkManager.Instance.JoinLanRoom(_ipInput.Text, port))
            {
                ShowScreen(_panelLanLobby);
            }
        };

        var btnCancel = CreateMenuButton(panel, "HỦY", new Vector2(320f, 240f), new Vector2(160f, 38f), false);
        btnCancel.Pressed += () => ShowScreen(_panelMultiplayerChoice);

        return panel;
    }

    private Control BuildSettingsPanel()
    {
        var panel = new Control { AnchorsPreset = (int)Control.LayoutPreset.FullRect };
        AddBadge(panel, "05 / CÀI ĐẶT");

        var btnSfx = CreateMenuButton(panel, "HIỆU ỨNG ÂM THANH (SFX): BẬT (85%)", new Vector2(42f, 110f), new Vector2(330f, 36f), false);
        btnSfx.Pressed += () =>
        {
            GameSession.Instance.SfxVolume = GameSession.Instance.SfxVolume > 0.1f ? 0f : 0.85f;
            btnSfx.Text = GameSession.Instance.SfxVolume > 0.1f
                ? "HIỆU ỨNG ÂM THANH (SFX): BẬT (85%)"
                : "HIỆU ỨNG ÂM THANH (SFX): TẮT (0%)";
        };

        var btnTrajectory = CreateMenuButton(panel, "ĐƯỜNG DỰ ĐOÁN QUỸ ĐẠO NHẢY: BẬT", new Vector2(42f, 156f), new Vector2(330f, 36f), false);
        btnTrajectory.Pressed += () =>
        {
            GameSession.Instance.ShowTrajectoryPreview = !GameSession.Instance.ShowTrajectoryPreview;
            btnTrajectory.Text = GameSession.Instance.ShowTrajectoryPreview
                ? "ĐƯỜNG DỰ ĐOÁN QUỸ ĐẠO NHẢY: BẬT"
                : "ĐƯỜNG DỰ ĐOÁN QUỸ ĐẠO NHẢY: TẮT";
        };

        var btnSelectGender = CreateMenuButton(panel, "NHÂN VẬT MẶC ĐỊNH: NAM (MALE EXPLORER)", new Vector2(42f, 202f), new Vector2(330f, 36f), false);
        btnSelectGender.Pressed += () =>
        {
            var slot0 = GameSession.Instance.Slots[0];
            slot0.Gender = slot0.Gender == CharacterGender.Male ? CharacterGender.Female : CharacterGender.Male;
            btnSelectGender.Text = slot0.Gender == CharacterGender.Male
                ? "NHÂN VẬT MẶC ĐỊNH: NAM (MALE EXPLORER)"
                : "NHÂN VẬT MẶC ĐỊNH: NỮ (FEMALE EXPLORER)";
        };

        var controlsLbl = new Label
        {
            Text = "ĐIỀU KHIỂN: A / D (Đi bộ & Chọn hướng)  |  SPACE / W (Giữ tích lực, Nhả để nhảy)",
            Position = new Vector2(42f, 252f),
            Modulate = new Color("f1faee")
        };
        controlsLbl.AddThemeFontSizeOverride("font_size", 10);
        panel.AddChild(controlsLbl);

        var btnBack = CreateMenuButton(panel, "ÁP DỤNG & QUAY LẠI", new Vector2(42f, 286f), new Vector2(220f, 36f), true);
        btnBack.Pressed += () => ShowScreen(_panelMain);

        return panel;
    }

    public void RefreshLobbyUi()
    {
        if (_lobbyInfoLabel == null)
        {
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            var slot = GameSession.Instance.Slots[i];
            if (_slotLabels[i] != null)
            {
                string genderStr = slot.Gender == CharacterGender.Female ? "Nữ - Female Explorer" : "Nam - Male Explorer";
                if (slot.IsOccupied)
                {
                    string role = i == 0 ? "CHỦ PHÒNG" : $"NGƯỜI CHƠI (ID {slot.PeerId})";
                    _slotLabels[i].Text = $"{role}  ·  [{genderStr}]";
                }
                else
                {
                    _slotLabels[i].Text = "CHỜ NGƯỜI CHƠI...";
                }
            }
        }

        string comboText = GameSession.Instance.SelectedCombination switch
        {
            MapCombination.CombinationA_JungleThenRock => "MAP: TỔ HỢP A (RỪNG → NÚI → ĐỀN)",
            MapCombination.CombinationB_RockThenJungle => "MAP: TỔ HỢP B (NÚI → RỪNG → ĐỀN)",
            _ => "MAP: NGẪU NHIÊN (TỔ HỢP A / B)"
        };
        _btnComboToggle.Text = comboText;

        _btnModeToggle.Text = GameSession.Instance.CurrentMode == GameMode.PvPQuickMatch
            ? "CHẾ ĐỘ: PVP QUICK MATCH (3 PHÚT)"
            : "CHẾ ĐỘ: PVP DEFAULT (TRANH ĐÍCH)";

        string ip = NetworkManager.Instance.GetLocalLanIp();
        int port = NetworkManager.Instance.ActivePort;
        _lobbyInfoLabel.Text =
            $"THÔNG TIN PHÒNG\n" +
            $"IP LAN: {ip}\n" +
            $"Cổng: {port} (Mặc định 7777)\n" +
            $"Trạng thái: {NetworkManager.Instance.StatusMessage}";

        _btnStartLanMatch.Disabled = !NetworkManager.Instance.IsHost;
    }

    private static void AddBadge(Control parent, string text)
    {
        var rect = new ColorRect
        {
            Color = new Color("18293b"),
            Position = new Vector2(42f, 14f),
            Size = new Vector2(145f, 20f)
        };
        parent.AddChild(rect);

        var lbl = new Label
        {
            Text = text,
            Position = new Vector2(48f, 17f),
            Modulate = new Color("56cfe1")
        };
        lbl.AddThemeFontSizeOverride("font_size", 9);
        parent.AddChild(lbl);
    }

    private static Button CreateMenuButton(Control parent, string text, Vector2 pos, Vector2 size, bool highlightGold)
    {
        var btn = new Button
        {
            Text = text,
            Position = pos,
            Size = size,
            Modulate = highlightGold ? new Color("ffd166") : new Color("e0fbfc")
        };
        btn.AddThemeFontSizeOverride("font_size", 10);
        parent.AddChild(btn);
        return btn;
    }
}
