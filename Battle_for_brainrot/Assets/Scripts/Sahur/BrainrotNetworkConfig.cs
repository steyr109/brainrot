using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public static class BrainrotNetworkConfig
{
    public const string ServerAddressPrefsKey = "BattleForBrainrot.ServerAddress";
    public const string ServerPortPrefsKey = "BattleForBrainrot.ServerPort";
    public const string PvpOwnsLeftKey = "BattleForBrainrot.PvpOwnsLeft";
    public const string PvpConnectionModeKey = "BattleForBrainrot.PvpConnectionMode";
    public const string PvpConnectionModeOnline = "Online";
    public const string PvpConnectionModeLan = "LAN";
    public const string PvpLanRoleKey = "BattleForBrainrot.PvpLanRole";
    public const string PvpLanRoleHost = "Host";
    public const string PvpLanRoleClient = "Client";
    public const string LanAddressPrefsKey = "BattleForBrainrot.LanAddress";


    private const string DefaultServerAddress = "176.123.162.77";
    private const ushort DefaultPort = 7777;

    public static string ServerAddress => PlayerPrefs.GetString(ServerAddressPrefsKey, DefaultServerAddress);
    public static ushort ServerPort => (ushort)Mathf.Clamp(PlayerPrefs.GetInt(ServerPortPrefsKey, DefaultPort), 1, 65535);
    public static string LanAddress => PlayerPrefs.GetString(LanAddressPrefsKey, "127.0.0.1");
    public static bool IsLanMode => PlayerPrefs.GetString(PvpConnectionModeKey, PvpConnectionModeLan) == PvpConnectionModeLan;
    public static bool IsLanHost => IsLanMode && PlayerPrefs.GetString(PvpLanRoleKey, PvpLanRoleHost) == PvpLanRoleHost;

    public static bool IsDedicatedServer
    {
        get
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-server" || args[i] == "--server" || args[i] == "-dedicatedServer")
                    return true;
            }

            return Application.isBatchMode;
        }
    }

    public static NetworkManager EnsureNetworkManager(bool serverMode)
    {
        if (NetworkManager.Singleton != null)
        {
            EnsureNetworkConfig(NetworkManager.Singleton);
            if (!NetworkManager.Singleton.IsListening)
                ConfigureTransport(serverMode);
            return NetworkManager.Singleton;
        }

        GameObject managerObject = new GameObject(serverMode ? "Brainrot Dedicated Network Manager" : "Brainrot Client Network Manager");
        managerObject.SetActive(false);
        Object.DontDestroyOnLoad(managerObject);

        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        EnsureNetworkConfig(manager);

        managerObject.SetActive(true);
        ConfigureTransport(serverMode);
        return manager;
    }

    private static void EnsureNetworkConfig(NetworkManager manager)
    {
        UnityTransport transport = manager.GetComponent<UnityTransport>();
        if (transport == null)
            transport = manager.gameObject.AddComponent<UnityTransport>();

        if (manager.NetworkConfig == null)
            manager.NetworkConfig = new NetworkConfig();

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.EnableSceneManagement = false;
    }

    public static void UseOnlineClient()
    {
        PlayerPrefs.SetString(PvpConnectionModeKey, PvpConnectionModeOnline);
        PlayerPrefs.Save();
        ConfigureTransport(false);
    }

    public static void UseOnlineClient(string address)
    {
        PlayerPrefs.SetString(PvpConnectionModeKey, PvpConnectionModeOnline);
        if (!string.IsNullOrWhiteSpace(address))
            PlayerPrefs.SetString(ServerAddressPrefsKey, address.Trim());
        PlayerPrefs.Save();
        ConfigureTransport(false);
    }

    public static void UseLanHost()
    {
        PlayerPrefs.SetString(PvpConnectionModeKey, PvpConnectionModeLan);
        PlayerPrefs.SetString(PvpLanRoleKey, PvpLanRoleHost);
        PlayerPrefs.Save();
        ConfigureTransport(false);
    }

    public static void UseLanClient(string address)
    {
        PlayerPrefs.SetString(PvpConnectionModeKey, PvpConnectionModeLan);
        PlayerPrefs.SetString(PvpLanRoleKey, PvpLanRoleClient);
        PlayerPrefs.SetString(LanAddressPrefsKey, string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim());
        PlayerPrefs.Save();
        ConfigureTransport(false);
    }

    public static void ConfigureTransport(bool serverMode)
    {
        if (NetworkManager.Singleton == null)
            return;

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
            transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();

        if (serverMode || IsLanHost)
            transport.SetConnectionData("0.0.0.0", ServerPort, "0.0.0.0");
        else if (IsLanMode)
            transport.SetConnectionData(LanAddress, ServerPort, "0.0.0.0");
        else
            transport.SetConnectionData(ServerAddress, ServerPort, "0.0.0.0");
    }
}
