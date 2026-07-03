using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// Lightweight UDP LAN game discovery.
/// Host calls StartHostBroadcast() → broadcasts availability every 2s.
/// Client calls StartClientScan() → listens for responses and populates DiscoveredGames.
/// Both sides call Stop() on teardown.
public class LANDiscovery : MonoBehaviour
{
    public static LANDiscovery Instance { get; private set; }

    private const int DiscoveryPort = 47777;
    private const string MagicDiscover = "EVOSIM_DISCOVER";
    private const string MagicGame     = "EVOSIM_GAME";
    private const string MagicLobby    = "EVOSIM_LOBBY";
    private const string MagicStart    = "EVOSIM_START";
    private const float BroadcastInterval = 2f;

    // ── Lobby / start state (updated by incoming broadcasts) ─────────────────
    public string LastLobbyBroadcast { get; private set; }
    public int    LastSeed           { get; private set; }
    public bool   GameStartSignaled  { get; private set; }

    [System.Serializable]
    public struct GameEntry
    {
        public string hostName;
        public string hostIP;
        public int    port;
        public int    playerCount;
        public float  lastSeen; // Time.time
    }

    public List<GameEntry> DiscoveredGames { get; } = new List<GameEntry>();

    private UdpClient    _udp;
    private Thread       _listenThread;
    private bool         _running;
    private bool         _isHost;
    private float        _broadcastTimer;
    private int          _gamePort = 7777;
    private int          _playerCount = 1;

    // Thread-safe queue for received packets
    private readonly Queue<string> _incoming = new Queue<string>();
    private readonly object _lock = new object();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy() => Stop();

    // ── Public API ─────────────────────────────────────────────────────────────

    public void StartHostBroadcast(int gamePort, int playerCount)
    {
        Stop();
        _isHost      = true;
        _gamePort    = gamePort;
        _playerCount = playerCount;
        _running     = true;
        _udp = new UdpClient();
        _udp.EnableBroadcast = true;
        _broadcastTimer = BroadcastInterval; // fire immediately on first Update
    }

    public void StartClientScan()
    {
        Stop();
        _isHost  = false;
        _running = true;
        DiscoveredGames.Clear();

        _udp = new UdpClient(DiscoveryPort);
        _udp.EnableBroadcast = true;
        _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "LANDiscovery" };
        _listenThread.Start();

        // Also send a discover ping so hosts respond immediately
        SendDiscoverPing();
    }

    public void RefreshScan()
    {
        DiscoveredGames.Clear();
        SendDiscoverPing();
    }

    public void Stop()
    {
        _running = false;
        _listenThread?.Interrupt();
        _listenThread = null;
        try { _udp?.Close(); } catch { }
        _udp = null;
    }

    public void UpdatePlayerCount(int count) => _playerCount = count;

    /// Host: sends current lobby slot config + ready flags + world seed to all clients.
    public void BroadcastLobbyState(MainMenuManager.SlotType[] slots, bool[] ready, int seed)
    {
        if (_udp == null || !_isHost) return;
        // Format: EVOSIM_LOBBY:seed:slot0type,slot1type,...:slot0ready,slot1ready,...
        var types = string.Join(",", System.Array.ConvertAll(slots, s => (int)s));
        var rdy   = string.Join(",", System.Array.ConvertAll(ready, r => r ? "1" : "0"));
        string msg = $"{MagicLobby}:{seed}:{types}:{rdy}";
        SendBroadcast(msg);
    }

    /// Host: signals all clients to start the game with the given world seed.
    public void BroadcastGameStart(int seed)
    {
        if (_udp == null) return;
        string msg = $"{MagicStart}:{seed}";
        SendBroadcast(msg);
    }

    private void SendBroadcast(string msg)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(msg);
            var ep = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            _udp.Send(data, data.Length, ep);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LANDiscovery] Send failed: {e.Message}");
        }
    }

    // ── Unity update ───────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_running) return;

        // Drain thread-safe incoming queue on main thread
        lock (_lock)
        {
            while (_incoming.Count > 0)
                HandlePacket(_incoming.Dequeue());
        }

        // Host: broadcast availability at regular intervals
        if (_isHost)
        {
            _broadcastTimer += Time.deltaTime;
            if (_broadcastTimer >= BroadcastInterval)
            {
                _broadcastTimer = 0f;
                BroadcastGamePresence();
            }
        }

        // Expire stale entries (not seen for >6s)
        for (int i = DiscoveredGames.Count - 1; i >= 0; i--)
            if (Time.time - DiscoveredGames[i].lastSeen > 6f)
                DiscoveredGames.RemoveAt(i);
    }

    // ── Networking ─────────────────────────────────────────────────────────────

    private void BroadcastGamePresence()
    {
        string hostName = SystemInfo.deviceName;
        SendBroadcast($"{MagicGame}:{hostName}:{_gamePort}:{_playerCount}");
    }

    private void SendDiscoverPing()
    {
        try
        {
            using var ping = new UdpClient();
            ping.EnableBroadcast = true;
            byte[] data = Encoding.UTF8.GetBytes(MagicDiscover);
            ping.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LANDiscovery] Ping failed: {e.Message}");
        }
    }

    private void ListenLoop()
    {
        var remoteEP = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[] data = _udp.Receive(ref remoteEP);
                string msg = Encoding.UTF8.GetString(data);
                // Ignore our own discover pings
                if (!msg.StartsWith(MagicDiscover))
                {
                    lock (_lock) _incoming.Enqueue($"{remoteEP.Address}|{msg}");
                }
            }
            catch (ThreadInterruptedException) { break; }
            catch (SocketException) { if (!_running) break; }
            catch (Exception e)
            {
                if (_running) Debug.LogWarning($"[LANDiscovery] Listen error: {e.Message}");
            }
        }
    }

    private void HandlePacket(string raw)
    {
        // Format: "ip|<MAGIC>:..."
        int sep = raw.IndexOf('|');
        if (sep < 0) return;
        string ip  = raw[..sep];
        string msg = raw[(sep + 1)..];

        if (msg.StartsWith(MagicGame + ":"))
        {
            HandleGamePresence(ip, msg);
        }
        else if (msg.StartsWith(MagicLobby + ":"))
        {
            // EVOSIM_LOBBY:seed:types:ready
            LastLobbyBroadcast = msg;
            string[] parts = msg.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int seed))
                LastSeed = seed;
        }
        else if (msg.StartsWith(MagicStart + ":"))
        {
            string[] parts = msg.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int seed))
                LastSeed = seed;
            GameStartSignaled = true;
        }
    }

    private void HandleGamePresence(string ip, string msg)
    {
        string[] parts = msg.Split(':');
        if (parts.Length < 4) return;
        if (!int.TryParse(parts[2], out int port))    return;
        if (!int.TryParse(parts[3], out int players)) return;

        string hostName = parts[1];
        for (int i = 0; i < DiscoveredGames.Count; i++)
        {
            if (DiscoveredGames[i].hostIP == ip)
            {
                var e = DiscoveredGames[i];
                e.hostName    = hostName;
                e.port        = port;
                e.playerCount = players;
                e.lastSeen    = Time.time;
                DiscoveredGames[i] = e;
                return;
            }
        }
        DiscoveredGames.Add(new GameEntry
        {
            hostName = hostName, hostIP = ip, port = port,
            playerCount = players, lastSeen = Time.time,
        });
    }
}
