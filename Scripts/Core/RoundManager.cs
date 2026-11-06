using System;
using System.Collections.Generic;
using System.Linq;
using BounceBash.Camera;
using BounceBash.Core;
using BounceBash.Gameplay;
using BounceBash.Level;
using BounceBash.Net;
using BounceBash.Player;
using BounceBash.UI;
using Godot;

namespace BounceBash.Core;

/// <summary>
/// Central RoundManager / GameFlow controller (GDD Section 1.7.2 & Figure 1.7-01):
/// Coordinates Menu -> MapAssembler -> Player Spawning -> Authoritative Simulation
/// (Physics, CameraRig, LivesRespawnSystem, ItemSystem, GoalZone) -> Match Result & Retry.
/// </summary>
public partial class RoundManager : Node2D
{
    private MapAssembler _mapAssembler = null!;
    private Node2D _playersContainer = null!;
    private ItemSystem _itemSystem = null!;
    private GoalZone _goalZone = null!;
    private CameraRig _cameraRig = null!;
    private MainMenuUI _mainMenu = null!;
    private GameHud _gameHud = null!;

    private readonly List<PlayerController> _activePlayers = [];
    private bool _isMatchRunning;
    private float _quickMatchTimer;

    public override void _Ready()
    {
        _mapAssembler = new MapAssembler { Name = "MapAssembler" };
        AddChild(_mapAssembler);

        _itemSystem = new ItemSystem { Name = "ItemSystem" };
        AddChild(_itemSystem);

        _goalZone = new GoalZone { Name = "GoalZone" };
        AddChild(_goalZone);

        _playersContainer = new Node2D { Name = "PlayersContainer" };
        AddChild(_playersContainer);

        _cameraRig = new CameraRig { Name = "CameraRig" };
        AddChild(_cameraRig);

        _gameHud = new GameHud { Name = "GameHud", Visible = false };
        _gameHud.RetryMatchRequested += OnRetryMatchRequested;
        _gameHud.ReturnToMenuRequested += ReturnToMainMenu;
        AddChild(_gameHud);

        _mainMenu = new MainMenuUI { Name = "MainMenuUI" };
        _mainMenu.StartSinglePlayerRequested += StartSinglePlayerMatch;
        _mainMenu.StartLocalTwoPlayerRequested += StartLocalTwoPlayerMatch;
        _mainMenu.HostStartLanMatchRequested += HostStartLanMatch;
        AddChild(_mainMenu);

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.MatchStartRequested += OnNetworkMatchStartRequested;
            NetworkManager.Instance.ClientInputReceived += OnClientInputReceived;
            NetworkManager.Instance.AuthoritativeStateReceived += OnAuthoritativeStateReceived;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Escape && _isMatchRunning)
            {
                ReturnToMainMenu();
            }
            else if (keyEvent.Keycode == Key.R && _isMatchRunning && (!NetworkManager.Instance.IsLanActive || NetworkManager.Instance.IsHost))
            {
                OnRetryMatchRequested();
            }
        }
    }

    private void StartSinglePlayerMatch()
    {
        NetworkManager.Instance.CloseLanSession();
        var defaultGender = GameSession.Instance.Slots[0].Gender;
        GameSession.Instance.ResetSlotsForSinglePlayer(defaultGender);
        GameSession.Instance.CurrentMode = GameMode.SinglePlayer;
        int seed = (int)(Time.GetTicksMsec() & 0x7FFFFFFF);
        BeginRound(seed, GameMode.SinglePlayer, GameSession.Instance.SelectedCombination);
    }

    private void StartLocalTwoPlayerMatch()
    {
        NetworkManager.Instance.CloseLanSession();
        var p1Gender = GameSession.Instance.Slots[0].Gender;
        var p2Gender = p1Gender == CharacterGender.Male ? CharacterGender.Female : CharacterGender.Male;
        GameSession.Instance.SetupSlotsForLocalTwoPlayer(p1Gender, p2Gender);
        GameSession.Instance.CurrentMode = GameMode.LocalTwoPlayerTest;
        int seed = (int)(Time.GetTicksMsec() & 0x7FFFFFFF);
        BeginRound(seed, GameMode.LocalTwoPlayerTest, GameSession.Instance.SelectedCombination);
    }

    private void HostStartLanMatch()
    {
        if (!NetworkManager.Instance.IsHost)
        {
            return;
        }

        // Ensure at least P1 is active; if testing LAN alone on 1 machine, still allow launching
        if (GameSession.Instance.CurrentMode == GameMode.SinglePlayer)
        {
            GameSession.Instance.CurrentMode = GameMode.PvPDefault;
        }

        int seed = (int)(Time.GetTicksMsec() & 0x7FFFFFFF);
        NetworkManager.Instance.HostBroadcastStartMatch(
            seed,
            GameSession.Instance.CurrentMode,
            GameSession.Instance.SelectedCombination);
    }

    private void OnNetworkMatchStartRequested(int seed, int modeInt, int combinationInt)
    {
        BeginRound(seed, (GameMode)modeInt, (MapCombination)combinationInt);
    }

    private void OnRetryMatchRequested()
    {
        int newSeed = (int)(Time.GetTicksMsec() & 0x7FFFFFFF);
        if (NetworkManager.Instance.IsLanActive)
        {
            if (NetworkManager.Instance.IsHost)
            {
                NetworkManager.Instance.HostBroadcastStartMatch(
                    newSeed,
                    GameSession.Instance.CurrentMode,
                    GameSession.Instance.SelectedCombination);
            }
        }
        else
        {
            BeginRound(newSeed, GameSession.Instance.CurrentMode, GameSession.Instance.SelectedCombination);
        }
    }

    private void ReturnToMainMenu()
    {
        _isMatchRunning = false;
        _gameHud.HideMatchResult();
        _gameHud.Visible = false;
        _mainMenu.Visible = true;
    }

    public void BeginRound(int seed, GameMode mode, MapCombination combination)
    {
        GameSession.Instance.CurrentMapSeed = seed;
        GameSession.Instance.CurrentMode = mode;

        _mainMenu.Visible = false;
        _gameHud.Visible = true;
        _gameHud.HideMatchResult();

        // 1. Assemble 3-Sector Modular Map (Jungle / Rock / Temple)
        _mapAssembler.BuildTower(seed, combination);

        // 2. Initialize Buff/Debuff Item System & Temple GoalZone
        _itemSystem.InitializeItemsFromAnchors(_mapAssembler.AllItemAnchors);
        _goalZone.ConfigureGoalPedestal(_mapAssembler.GoalZonePosition);

        // 3. Spawn Active Players (P1..P4)
        foreach (Node child in _playersContainer.GetChildren())
        {
            child.QueueFree();
        }
        _activePlayers.Clear();

        foreach (var slot in GameSession.Instance.Slots)
        {
            if (!slot.IsOccupied)
            {
                continue;
            }

            var player = new PlayerController
            {
                Name = $"Player_{slot.DisplayName}",
                SlotIndex = slot.SlotIndex,
                PeerId = slot.PeerId,
                PlayerTag = slot.DisplayName,
                TagColor = slot.TagColor,
                Gender = slot.Gender,
                IsLocalSecondaryKeyboard = mode == GameMode.LocalTwoPlayerTest && slot.SlotIndex == 1,
                GlobalPosition = _mapAssembler.SpawnPositions[slot.SlotIndex]
            };
            _playersContainer.AddChild(player);
            _activePlayers.Add(player);
        }

        // 4. Reset CameraRig
        _cameraRig.ResetCamera(0f, _mapAssembler.TowerTopY);
        _quickMatchTimer = GameSession.Instance.Tuning.QuickMatchDurationSeconds;

        _gameHud.BindMatchState(_activePlayers, _goalZone, _itemSystem, _mapAssembler, _quickMatchTimer);
        _isMatchRunning = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isMatchRunning || _activePlayers.Count == 0)
        {
            return;
        }

        float dt = (float)delta;
        bool isAuthoritative = !NetworkManager.Instance.IsLanActive || NetworkManager.Instance.IsHost;

        // 1. Sample Local Input
        int localPeerId = NetworkManager.Instance.IsLanActive ? Multiplayer.GetUniqueId() : 1;
        foreach (var player in _activePlayers)
        {
            if (GameSession.Instance.CurrentMode == GameMode.LocalTwoPlayerTest || player.PeerId == localPeerId)
            {
                player.SampleLocalInput();
                if (NetworkManager.Instance.IsLanActive && !NetworkManager.Instance.IsHost)
                {
                    NetworkManager.Instance.SendClientInputToHost(
                        player.NetMoveInput,
                        player.NetJumpHeld,
                        player.NetJumpReleasedBuffer);
                    player.NetJumpReleasedBuffer = false;
                }
            }
        }

        // 2. Simulate Players
        foreach (var player in _activePlayers)
        {
            player.SimulateTick(dt, isAuthoritative);
        }

        if (!isAuthoritative)
        {
            _gameHud.RefreshHud(_quickMatchTimer);
            return;
        }

        // 3. Authoritative Host updates CameraRig, Lives/Respawn (Main/Aux), Items, and GoalZone
        bool isPvP = GameSession.Instance.CurrentMode != GameMode.SinglePlayer;
        _cameraRig.UpdateCamera(dt, _activePlayers, GameSession.Instance.CurrentMode);

        LivesRespawnSystem.UpdateMainAuxAndCheckFalls(
            _activePlayers,
            _mapAssembler.AllRespawnPlatforms,
            _cameraRig,
            GameSession.Instance.Tuning,
            isPvP);

        _itemSystem.UpdateItems(dt, _activePlayers);
        _goalZone.EvaluateGoalZone(dt, _activePlayers, GameSession.Instance.CurrentMode);

        if (GameSession.Instance.CurrentMode == GameMode.PvPQuickMatch)
        {
            _quickMatchTimer = Mathf.Max(0f, _quickMatchTimer - dt);
        }

        _gameHud.RefreshHud(_quickMatchTimer);

        // 4. Broadcast authoritative snapshot if LAN is active
        if (NetworkManager.Instance.IsLanActive && NetworkManager.Instance.IsHost)
        {
            BroadcastHostSnapshot();
        }

        // 5. Evaluate Match Completion Rules (GDD Section 1.2.2 & 1.2.5)
        CheckMatchEndConditions();
    }

    private void CheckMatchEndConditions()
    {
        var mode = GameSession.Instance.CurrentMode;

        // Case 1: GoalZone winner (Singleplayer touched Elixir, or PvP personal hold timer reached 0)
        if (_goalZone.Winner != null)
        {
            var w = _goalZone.Winner;
            _isMatchRunning = false;
            _gameHud.ShowMatchResult(
                $"VICTORY — {w.PlayerTag} CHIẾN THẮNG!",
                $"{w.PlayerTag} đã chiếm giữ thành công Bảo Vật Trường Sinh trên đỉnh Đền!\n" +
                $"Lượt sống còn lại: {w.RemainingLives}/3",
                w.TagColor);
            return;
        }

        // Case 2: Singleplayer lost all 3 lives
        if (mode == GameMode.SinglePlayer)
        {
            if (_activePlayers[0].IsEliminated)
            {
                _isMatchRunning = false;
                _gameHud.ShowMatchResult(
                    "THẤT BẠI — KIỆT SỨC!",
                    "Nhà thám hiểm đã rơi khỏi khung hình quá 2 lần (hết 3 lượt).\nNhấn Chơi Lại để thử thách lại tuyến leo!",
                    new Color("e63946"));
            }
            return;
        }

        // Case 3: PvP Elimination survival (GDD 1.2.2: if only 1 player remains non-eliminated, they win)
        var survivors = _activePlayers.Where(p => !p.IsEliminated).ToList();
        if (_activePlayers.Count >= 2 && survivors.Count == 1)
        {
            var w = survivors[0];
            _isMatchRunning = false;
            GameSession.Instance.PlaySfx("sfx_victory", 1.0f, 0.9f);
            _gameHud.ShowMatchResult(
                $"VICTORY — {w.PlayerTag} CHIẾN THẮNG!",
                $"Tất cả đối thủ đã kiệt sức (mất 3 lượt)!\n{w.PlayerTag} là nhà thám hiểm cuối cùng trụ lại trên đường leo.",
                w.TagColor);
            return;
        }

        if (survivors.Count == 0)
        {
            _isMatchRunning = false;
            _gameHud.ShowMatchResult(
                "HÒA — CÙNG KIỆT SỨC!",
                "Tất cả người chơi đã mất lượt cuối cùng trong cùng một tích tắc vật lý!",
                new Color("ffd166"));
            return;
        }

        // Case 4: PvP Quick Match tiebreaker when timer hits 0 (GDD 1.2.5)
        if (mode == GameMode.PvPQuickMatch && _quickMatchTimer <= 0f)
        {
            var ranked = survivors
                .OrderBy(p => p.PersonalGoalTimer) // Most progress holding GoalZone
                .ThenBy(p => p.GlobalPosition.Y)   // Highest altitude (smallest Y)
                .ToList();
            var w = ranked[0];
            _isMatchRunning = false;
            GameSession.Instance.PlaySfx("sfx_victory", 1.0f, 0.9f);
            _gameHud.ShowMatchResult(
                $"HẾT GIỜ — {w.PlayerTag} THẮNG TIE-BREAK!",
                $"Xét theo tiến độ giữ đích ({w.PersonalGoalTimer:0.0}s) và độ cao hiện tại.",
                w.TagColor);
        }
    }

    private void OnClientInputReceived(int peerId, float moveInput, bool jumpHeld, bool jumpReleased)
    {
        var player = _activePlayers.FirstOrDefault(p => p.PeerId == peerId);
        if (player != null)
        {
            player.NetMoveInput = moveInput;
            player.NetJumpHeld = jumpHeld;
            if (jumpReleased)
            {
                player.NetJumpReleasedBuffer = true;
            }
        }
    }

    private void BroadcastHostSnapshot()
    {
        var dict = new Godot.Collections.Dictionary
        {
            ["cam_y"] = _cameraRig.GlobalPosition.Y,
            ["qm_timer"] = _quickMatchTimer
        };

        var pList = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var p in _activePlayers)
        {
            pList.Add(new Godot.Collections.Dictionary
            {
                ["slot"] = p.SlotIndex,
                ["x"] = p.GlobalPosition.X,
                ["y"] = p.GlobalPosition.Y,
                ["vx"] = p.Velocity.X,
                ["vy"] = p.Velocity.Y,
                ["lives"] = p.RemainingLives,
                ["elim"] = p.IsEliminated,
                ["goal_t"] = p.PersonalGoalTimer
            });
        }
        dict["players"] = pList;

        NetworkManager.Instance.BroadcastAuthoritativeSnapshot(dict);
    }

    private void OnAuthoritativeStateReceived(Godot.Collections.Dictionary snapshot)
    {
        if (NetworkManager.Instance.IsHost)
        {
            return;
        }

        if (snapshot.TryGetValue("cam_y", out var camYVar))
        {
            _cameraRig.ApplyAuthoritativeCameraY((float)camYVar.AsDouble());
        }
        if (snapshot.TryGetValue("qm_timer", out var qmVar))
        {
            _quickMatchTimer = (float)qmVar.AsDouble();
        }
        if (snapshot.TryGetValue("players", out var pArrVar))
        {
            var arr = pArrVar.AsGodotArray<Godot.Collections.Dictionary>();
            foreach (var pDict in arr)
            {
                int slot = pDict["slot"].AsInt32();
                var player = _activePlayers.FirstOrDefault(p => p.SlotIndex == slot);
                if (player != null)
                {
                    player.GlobalPosition = new Vector2((float)pDict["x"].AsDouble(), (float)pDict["y"].AsDouble());
                    player.Velocity = new Vector2((float)pDict["vx"].AsDouble(), (float)pDict["vy"].AsDouble());
                    player.RemainingLives = pDict["lives"].AsInt32();
                    player.IsEliminated = pDict["elim"].AsBool();
                    player.PersonalGoalTimer = (float)pDict["goal_t"].AsDouble();
                }
            }
        }
    }
}
