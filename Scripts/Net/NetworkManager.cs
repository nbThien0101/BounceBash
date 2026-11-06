using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BounceBash.Core;
using Godot;

namespace BounceBash.Net;

/// <summary>
/// Host-Authoritative LAN Session & State Sync Manager using Godot's ENetMultiplayerPeer
/// (GDD Section 1.6.3 & 1.7.3 - replacing Unity Netcode for GameObjects + Unity Transport).
/// - Default Port: 7777 (configurable when creating or joining a LAN room)
/// - Host automatically occupies P1 (Red); Clients occupy P2-P4.
/// - Client sends input to Host; Host simulates physics, collisions, camera, respawn, items, and GoalZone,
///   then broadcasts state snapshots to all Clients.
/// </summary>
public partial class NetworkManager : Node
{
    public static NetworkManager Instance { get; private set; } = null!;

    public const int DefaultLanPort = 7777;
    public const int MaxPlayers = 4;

    public bool IsLanActive { get; private set; }
    public bool IsHost { get; private set; } = true;
    public int ActivePort { get; private set; } = DefaultLanPort;
    public string StatusMessage { get; private set; } = "Chưa kết nối LAN";

    public event Action? LobbyUpdated;
    public event Action<int, int, int>? MatchStartRequested;
    public event Action<int, float, bool, bool>? ClientInputReceived;
    public event Action<Godot.Collections.Dictionary>? AuthoritativeStateReceived;

    private ENetMultiplayerPeer? _enetPeer;

    public override void _Ready()
    {
        Instance = this;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public string GetLocalLanIp()
    {
        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch
        {
            // Fallback to localhost if offline
        }
        return "127.0.0.1";
    }

    public bool HostLanRoom(int port = DefaultLanPort)
    {
        CloseLanSession();

        ActivePort = port <= 0 ? DefaultLanPort : port;
        _enetPeer = new ENetMultiplayerPeer();
        Error err = _enetPeer.CreateServer(ActivePort, MaxPlayers - 1);
        if (err != Error.Ok)
        {
            StatusMessage = $"Lỗi tạo phòng LAN (Cổng {ActivePort}): {err}";
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

    public bool JoinLanRoom(string ipAddress, int port = DefaultLanPort)
    {
        CloseLanSession();

        string cleanIp = string.IsNullOrWhiteSpace(ipAddress) ? "127.0.0.1" : ipAddress.Trim();
        ActivePort = port <= 0 ? DefaultLanPort : port;

        _enetPeer = new ENetMultiplayerPeer();
        Error err = _enetPeer.CreateClient(cleanIp, ActivePort);
        if (err != Error.Ok)
        {
            StatusMessage = $"Không thể kết nối tới {cleanIp}:{ActivePort} ({err})";
            _enetPeer = null;
            return false;
        }

        Multiplayer.MultiplayerPeer = _enetPeer;
        IsLanActive = true;
        IsHost = false;
        StatusMessage = $"Đang kết nối tới {cleanIp}:{ActivePort}...";
        LobbyUpdated?.Invoke();
        return true;
    }

    public void CloseLanSession()
    {
        if (_enetPeer != null)
        {
            _enetPeer.Close();
            _enetPeer = null;
        }
        Multiplayer.MultiplayerPeer = null;
        IsLanActive = false;
        IsHost = true;
        StatusMessage = "Đã ngắt kết nối LAN";
    }

    private void OnPeerConnected(long id)
    {
        if (!IsHost)
        {
            return;
        }

        // Assign the first available slot (P2..P4) to the newly connected client
        foreach (var slot in GameSession.Instance.Slots)
        {
            if (!slot.IsOccupied)
            {
                slot.IsOccupied = true;
                slot.PeerId = (int)id;
                break;
            }
        }

        StatusMessage = $"Người chơi mới ({id}) đã vào phòng.";
        BroadcastLobbySlots();
    }

    private void OnPeerDisconnected(long id)
    {
        if (!IsHost)
        {
            return;
        }

        foreach (var slot in GameSession.Instance.Slots)
        {
            if (slot.IsOccupied && slot.PeerId == (int)id && slot.SlotIndex > 0)
            {
                slot.IsOccupied = false;
                slot.PeerId = 0;
            }
        }

        StatusMessage = $"Người chơi ({id}) đã rời phòng.";
        BroadcastLobbySlots();
    }

    private void OnConnectedToServer()
    {
        StatusMessage = "Đã kết nối vào Sảnh Chờ LAN!";
        LobbyUpdated?.Invoke();
    }

    private void OnConnectionFailed()
    {
        StatusMessage = "Kết nối LAN thất bại. Hãy kiểm tra IP và Cổng.";
        CloseLanSession();
        LobbyUpdated?.Invoke();
    }

    private void OnServerDisconnected()
    {
        StatusMessage = "Chủ phòng đã đóng phòng LAN.";
        CloseLanSession();
        LobbyUpdated?.Invoke();
    }

    public void RequestToggleLocalGender()
    {
        int myPeerId = IsLanActive ? Multiplayer.GetUniqueId() : 1;
        if (IsHost)
        {
            ToggleGenderForPeer(myPeerId);
            if (IsLanActive)
            {
                BroadcastLobbySlots();
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
        BroadcastLobbySlots();
    }

    private static void ToggleGenderForPeer(int peerId)
    {
        var slot = GameSession.Instance.Slots.FirstOrDefault(s => s.IsOccupied && s.PeerId == peerId);
        if (slot != null)
        {
            slot.Gender = slot.Gender == CharacterGender.Male ? CharacterGender.Female : CharacterGender.Male;
        }
    }

    public void BroadcastLobbySlots()
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
