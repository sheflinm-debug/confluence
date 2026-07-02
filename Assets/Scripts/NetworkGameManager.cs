using System.Net;
using UnityEngine;

/// Thin host/client wrapper.
/// Full network sync requires Unity Netcode for GameObjects (com.unity.netcode.gameobjects).
/// Until that package is added, this class provides the API surface that MainMenuManager uses,
/// compiling cleanly and logging informative placeholders.
///
/// To enable networking:
///   1. Open Package Manager → Add by name: com.unity.netcode.gameobjects
///   2. Uncomment the UNITY_NETCODE regions below and delete the stub implementations.
public class NetworkGameManager : MonoBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    public enum Role { None, Host, Client }
    public Role CurrentRole { get; private set; } = Role.None;
    public bool IsConnected { get; private set; }
    public int  ConnectedPlayerCount { get; private set; } = 1;

    private const int DefaultPort = 7777;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// Start as host on LAN. Initializes LANDiscovery broadcast.
    public void StartHost(PlanetConfig config)
    {
        CurrentRole = Role.Host;
        IsConnected = true;
        ConnectedPlayerCount = 1;

        // Broadcast availability
        if (LANDiscovery.Instance != null)
            LANDiscovery.Instance.StartHostBroadcast(DefaultPort, ConnectedPlayerCount);

#if UNITY_NETCODE_GAMEOBJECTS
        // NGO host start goes here once package is installed.
        // NetworkManager.Singleton.StartHost();
#else
        Debug.Log("[NetworkGameManager] NGO not installed — running as offline host (simulation is local only). " +
                  "Add com.unity.netcode.gameobjects via Package Manager to enable real multiplayer.");
#endif
    }

    /// Connect to an existing host by IP.
    public void JoinGame(string hostIP, int port = DefaultPort)
    {
        CurrentRole = Role.Client;

#if UNITY_NETCODE_GAMEOBJECTS
        // NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(hostIP, (ushort)port);
        // NetworkManager.Singleton.StartClient();
#else
        Debug.Log($"[NetworkGameManager] Would connect to {hostIP}:{port}. " +
                  "Install com.unity.netcode.gameobjects to enable this.");
        IsConnected = true; // stub
#endif
    }

    /// Gracefully disconnect.
    public void Disconnect()
    {
        CurrentRole = Role.None;
        IsConnected = false;
        ConnectedPlayerCount = 1;
        LANDiscovery.Instance?.Stop();

#if UNITY_NETCODE_GAMEOBJECTS
        // if (NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
#endif
    }

    // ── Seed sharing ──────────────────────────────────────────────────────────

    /// Host broadcasts the world seed so all clients generate the same world deterministically.
    /// With NGO this would be a ServerRpc/ClientRpc. Stub until NGO is installed.
    public void BroadcastWorldSeed(int seed)
    {
#if UNITY_NETCODE_GAMEOBJECTS
        // BroadcastSeedClientRpc(seed);
#else
        Debug.Log($"[NetworkGameManager] World seed {seed} — would broadcast to clients when NGO installed.");
#endif
    }

    private void OnDestroy() => Disconnect();
}
