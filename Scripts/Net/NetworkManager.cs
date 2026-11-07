using System;
using BounceBash.Core;
using Godot;

namespace BounceBash.Net;

/// <summary>
/// Host-Authoritative LAN Networking controller via ENetMultiplayerPeer (GDD Section 1.7.1 - 1.7.3 & Figure 1.7-01).
/// Features:
/// - Default Port: 7777 (configurable when creating or joining a LAN room)
/// - Host automatically occupies P1 (Red); Clients occupy P2-P4.
/// - Client sends input to Host; Host simulates physics, collisions, camera, respawn, items, and GoalZone,
///   then broadcasts state snapshots to all Clients.
/// </summary>
public partial class NetworkManager : Node
{
    public const int DefaultLanPort = 7777;

    public static NetworkManager Instance { get; private set; } = null!;

    public bool IsLanActive { get; private set; }
    public bool IsHost { get; private set; } = true;
    public int ActivePort { get; private set; } = DefaultLanPort;
    public string StatusMessage { get; private set; } = "Chưa kết nối LAN";

    public event Action? LobbyUpdated;
    public event Action? HostResetOccurred;
    public event Action<int, int, int>? MatchStartRequested;
    public event Action<int, float, bool, bool>? ClientInputReceived;
    public event Action<Godot.Collections.Dictionary>? AuthoritativeStateReceived;
    public event Action<string, string, Color>? MatchEndReceived;
    public event Action<string>? EliminationNoticeReceived;

    private ENetMultiplayerPeer? _enetPeer;

    public override void _Ready()
    {
        Instance = this;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;

        // Auto-load configured port from multiplayer_config.json if present
        ConfiguredPort = LoadPortFromConfigFile();
        ActivePort = ConfiguredPort;
    }

    public override void _ExitTree()
    {
        CloseLanSession();
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
    }

    public int ConfiguredPort { get; private set; } = DefaultLanPort;

    private static int LoadPortFromConfigFile()
    {
        string configPath = "res://multiplayer_config.json";
        if (!FileAccess.FileExists(configPath))
        {
            return DefaultLanPort;
        }

        try
        {
            using var file = FileAccess.Open(configPath, FileAccess.ModeFlags.Read);
            string jsonString = file.GetAsText();
            var json = Json.ParseString(jsonString);
            if (json.VariantType == Variant.Type.Dictionary)
            {
                var dict = json.AsGodotDictionary();
                if (dict.TryGetValue("lan_port", out var portVal))
                {
                    int parsed = portVal.AsInt32();
                    if (parsed is > 1024 and <= 65535)
                    {
                        return parsed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NetworkManager] Failed to load multiplayer_config.json: {ex.Message}");
        }

        return DefaultLanPort;
    }

    public string GetLocalLanIp()
    {
        string fallback = "127.0.0.1";
        try
        {
            foreach (string ip in IP.GetLocalAddresses())
            {
                if (ip.Contains(':'))
                {
                    continue; // Skip IPv6
                }
                if (ip.StartsWith("127."))
                {
                    continue; // Skip Loopback
                }
                if (ip.StartsWith("169.254."))
                {
                    continue; // Skip Link-Local
                }
                return ip;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NetworkManager] Error querying local IP: {ex.Message}");
        }
        return fallback;
    }

    public bool HostLanRoom(int port = DefaultLanPort)
    {
        CloseLanSession();
        ConfiguredPort = port;
        ActivePort = port;

        _enetPeer = new ENetMultiplayerPeer();
        var err = _enetPeer.CreateServer(port, 4); // Max 4 players (GDD 1.7.1)
        if (err != Error.Ok)
        {
            StatusMessage = $"Không thể mở phòng tại cổng {port} (Lỗi: {err})";
            GD.PrintErr($"[NetworkManager] CreateServer error: {err}");
            _enetPeer = null;
            return false;
        }

        Multiplayer.MultiplayerPeer = _enetPeer;
        IsLanActive = true;
        IsHost = true;
        StatusMessage = $"Đang làm Chủ Phòng P1 ({GetLocalLanIp()}:{ActivePort})";

        GameSession.Instance.ResetSlotsForSinglePlayer(CharacterGender.Male);
        GameSession.Instance.Slots[0].PeerId = 1;
        GameSession.Instance.Slots[0].IsOccupied = true;

        LobbyUpdated?.Invoke();
        return true;
    }

    public bool JoinLanRoom(string hostAddress, int port = DefaultLanPort)
    {
        CloseLanSession();
        ConfiguredPort = port;
        ActivePort = port;

        _enetPeer = new ENetMultiplayerPeer();
        var err = _enetPeer.CreateClient(hostAddress, port);
        if (err != Error.Ok)
        {
            StatusMessage = $"Không thể kết nối đến {hostAddress}:{port} (Lỗi: {err})";
            GD.PrintErr($"[NetworkManager] CreateClient error: {err}");
            _enetPeer = null;
            return false;
        }

        Multiplayer.MultiplayerPeer = _enetPeer;
        IsLanActive = true;
        IsHost = false;
        StatusMessage = $"Đang kết nối đến {hostAddress}:{port}...";

        LobbyUpdated?.Invoke();
        return true;
    }

    public void CloseLanSession()
    {
        if (_enetPeer != null)
        {
            _enetPeer.Close();
            _enetPeer.Dispose();
            _enetPeer = null;
        }
        Multiplayer.MultiplayerPeer = null;
        IsLanActive = false;
        IsHost = true;
        StatusMessage = "Chưa kết nối LAN";
    }

    public void ResetHostRoom()
    {
        if (!IsHost)
        {
            return;
        }

        CloseLanSession();

        var defaultGender = GameSession.Instance.Slots.Count > 0 ? GameSession.Instance.Slots[0].Gender : CharacterGender.Male;
        GameSession.Instance.ResetSlotsForSinglePlayer(defaultGender);

        HostLanRoom(ConfiguredPort);

        HostResetOccurred?.Invoke();
        LobbyUpdated?.Invoke();
    }

    public void CloseAndResetHostRoom()
    {
        if (IsLanActive && IsHost)
        {
            Rpc(MethodName.RpcNotifyHostReset);
        }

        CloseLanSession();

        var defaultGender = GameSession.Instance.Slots.Count > 0 ? GameSession.Instance.Slots[0].Gender : CharacterGender.Male;
        GameSession.Instance.ResetSlotsForSinglePlayer(defaultGender);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcNotifyHostReset()
    {
        CloseLanSession();
        HostResetOccurred?.Invoke();
    }

    private void OnPeerConnected(long peerIdLong)
    {
        int peerId = (int)peerIdLong;
        if (!IsHost)
        {
            return;
        }

        int assignedSlot = -1;
        for (int i = 1; i < 4; i++)
        {
            if (!GameSession.Instance.Slots[i].IsOccupied)
            {
                GameSession.Instance.Slots[i].IsOccupied = true;
                GameSession.Instance.Slots[i].PeerId = peerId;
                assignedSlot = i;
                break;
            }
        }

        if (assignedSlot != -1)
        {
            SyncLobbySlotsToAllClients();
        }
        else
        {
            _enetPeer?.DisconnectPeer(peerId);
        }
    }

    private void OnPeerDisconnected(long peerIdLong)
    {
        int peerId = (int)peerIdLong;
        if (!IsHost)
        {
            return;
        }

        for (int i = 1; i < 4; i++)
        {
            if (GameSession.Instance.Slots[i].PeerId == peerId)
            {
                GameSession.Instance.Slots[i].IsOccupied = false;
                GameSession.Instance.Slots[i].PeerId = 0;
                break;
            }
        }

        SyncLobbySlotsToAllClients();
    }

    private void OnConnectedToServer()
    {
        StatusMessage = $"Đã vào phòng LAN thành công! Cổng: {ActivePort}";
        LobbyUpdated?.Invoke();
    }

    private void OnConnectionFailed()
    {
        StatusMessage = "Kết nối đến phòng LAN thất bại! Vui lòng kiểm tra IP và tường lửa.";
        CloseLanSession();
        LobbyUpdated?.Invoke();
    }

    private void OnServerDisconnected()
    {
        StatusMessage = "Chủ phòng đã ngắt kết nối hoặc phòng đã bị đóng.";
        CloseLanSession();
        HostResetOccurred?.Invoke();
    }

    public void RequestToggleLocalGender()
    {
        int myPeerId = IsLanActive ? Multiplayer.GetUniqueId() : 1;
        if (IsHost)
        {
            ToggleGenderForPeer(myPeerId);
            if (IsLanActive)
            {
                SyncLobbySlotsToAllClients();
            }
            else
            {
                LobbyUpdated?.Invoke();
            }
        }
        else
        {
            RpcId(1, MethodName.RpcRequestToggleGender, myPeerId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcRequestToggleGender(int peerId)
    {
        if (!IsHost)
        {
            return;
        }
        ToggleGenderForPeer(peerId);
        SyncLobbySlotsToAllClients();
    }

    private static void ToggleGenderForPeer(int peerId)
    {
        var slot = GameSession.Instance.Slots.Find(s => s.PeerId == peerId && s.IsOccupied);
        if (slot != null)
        {
            slot.Gender = slot.Gender == CharacterGender.Male ? CharacterGender.Female : CharacterGender.Male;
        }
    }

    private void SyncLobbySlotsToAllClients()
    {
        if (!IsHost)
        {
            return;
        }

        var occupied = new Godot.Collections.Array<bool>();
        var peerIds = new Godot.Collections.Array<int>();
        var genders = new Godot.Collections.Array<int>();

        foreach (var s in GameSession.Instance.Slots)
        {
            occupied.Add(s.IsOccupied);
            peerIds.Add(s.PeerId);
            genders.Add((int)s.Gender);
        }

        if (IsLanActive)
        {
            Rpc(MethodName.RpcSyncLobbySlots, occupied, peerIds, genders);
        }
        LobbyUpdated?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncLobbySlots(
        Godot.Collections.Array<bool> occupied,
        Godot.Collections.Array<int> peerIds,
        Godot.Collections.Array<int> genders)
    {
        for (int i = 0; i < GameSession.Instance.Slots.Count && i < occupied.Count; i++)
        {
            GameSession.Instance.Slots[i].IsOccupied = occupied[i];
            GameSession.Instance.Slots[i].PeerId = peerIds[i];
            GameSession.Instance.Slots[i].Gender = (CharacterGender)genders[i];
        }
        LobbyUpdated?.Invoke();
    }

    public void HostBroadcastStartMatch(int seed, GameMode mode, MapCombination combination)
    {
        if (!IsHost)
        {
            return;
        }

        if (IsLanActive)
        {
            Rpc(MethodName.RpcStartMatch, seed, (int)mode, (int)combination);
        }
        MatchStartRequested?.Invoke(seed, (int)mode, (int)combination);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcStartMatch(int seed, int modeInt, int combinationInt)
    {
        MatchStartRequested?.Invoke(seed, modeInt, combinationInt);
    }

    public void HostBroadcastMatchEnd(string title, string detail, Color titleColor)
    {
        if (!IsHost)
        {
            return;
        }

        if (IsLanActive)
        {
            Rpc(MethodName.RpcMatchEnd, title, detail, titleColor.ToHtml());
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcMatchEnd(string title, string detail, string colorHtml)
    {
        Color titleColor = string.IsNullOrEmpty(colorHtml) ? Colors.White : new Color(colorHtml);
        MatchEndReceived?.Invoke(title, detail, titleColor);
    }

    public void HostBroadcastEliminationNotice(string notice)
    {
        if (!IsHost)
        {
            return;
        }

        if (IsLanActive)
        {
            Rpc(MethodName.RpcEliminationNotice, notice);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcEliminationNotice(string notice)
    {
        EliminationNoticeReceived?.Invoke(notice);
    }

    public void SendClientInputToHost(float moveInput, bool jumpHeld, bool jumpReleased)
    {
        if (!IsLanActive || IsHost)
        {
            return;
        }

        int myPeerId = Multiplayer.GetUniqueId();
        RpcId(1, MethodName.RpcReceiveClientInput, myPeerId, moveInput, jumpHeld, jumpReleased);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void RpcReceiveClientInput(int peerId, float moveInput, bool jumpHeld, bool jumpReleased)
    {
        if (!IsHost)
        {
            return;
        }
        ClientInputReceived?.Invoke(peerId, moveInput, jumpHeld, jumpReleased);
    }

    public void BroadcastAuthoritativeSnapshot(Godot.Collections.Dictionary snapshot)
    {
        if (!IsLanActive || !IsHost)
        {
            return;
        }
        Rpc(MethodName.RpcApplyAuthoritativeSnapshot, snapshot);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void RpcApplyAuthoritativeSnapshot(Godot.Collections.Dictionary snapshot)
    {
        AuthoritativeStateReceived?.Invoke(snapshot);
    }
}
