using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class SahurPvpNetworkController : MonoBehaviour
{
    private const string StateMessage = "SahurPvpState";
    private const string DamageMessage = "SahurPvpDamage";
    private const ushort Port = 7777;
    private const float SendInterval = 0.033f;

    private SahurFighterController localFighter;
    private SahurFighterController remoteFighter;
    private bool isHost;
    private float nextSendTime;

    public bool IsHost => isHost;

    public void Initialize(SahurFighterController local, SahurFighterController remote)
    {
        localFighter = local;
        remoteFighter = remote;
        isHost = Application.isEditor;
        EnsureNetworkManager();
        RegisterMessages();

        if (!NetworkManager.Singleton.IsListening)
        {
            if (isHost)
                NetworkManager.Singleton.StartHost();
            else
                NetworkManager.Singleton.StartClient();
        }
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        if (Time.time >= nextSendTime)
        {
            nextSendTime = Time.time + SendInterval;
            SendLocalState();
        }
    }

    private static void EnsureNetworkManager()
    {
        if (NetworkManager.Singleton != null)
            return;

        GameObject managerObject = new GameObject("Local PVP Network Manager");
        DontDestroyOnLoad(managerObject);

        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");

        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false
        };
    }

    private void RegisterMessages()
    {
        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(DamageMessage);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnStateMessage);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(DamageMessage, OnDamageMessage);
    }

    private void SendLocalState()
    {
        if (localFighter == null)
            return;

        Vector3 position = localFighter.transform.position;
        Quaternion rotation = localFighter.transform.rotation;

        using FastBufferWriter writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteValueSafe(position.x);
        writer.WriteValueSafe(position.y);
        writer.WriteValueSafe(position.z);
        writer.WriteValueSafe(rotation.eulerAngles.y);
        writer.WriteValueSafe(localFighter.CurrentHealth);
        writer.WriteValueSafe(localFighter.IsDefeated);
        writer.WriteValueSafe(localFighter.NetworkMoveSpeed);
        writer.WriteValueSafe(localFighter.NetworkGrounded);
        writer.WriteValueSafe(localFighter.NetworkVerticalVelocity);
        writer.WriteValueSafe(localFighter.NetworkBlocking);
        writer.WriteValueSafe(localFighter.LastAttackSequence);
        writer.WriteValueSafe(localFighter.LastAttackTrigger);

        if (NetworkManager.Singleton.IsHost)
        {
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.ServerClientId)
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(StateMessage, clientId, writer);
            }
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(StateMessage, NetworkManager.ServerClientId, writer);
        }
    }

    private void OnStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (remoteFighter == null)
            return;

        reader.ReadValueSafe(out float x);
        reader.ReadValueSafe(out float y);
        reader.ReadValueSafe(out float z);
        reader.ReadValueSafe(out float rotationY);
        reader.ReadValueSafe(out int health);
        reader.ReadValueSafe(out bool defeated);
        reader.ReadValueSafe(out float moveSpeed);
        reader.ReadValueSafe(out bool grounded);
        reader.ReadValueSafe(out float verticalVelocity);
        reader.ReadValueSafe(out bool blocking);
        reader.ReadValueSafe(out int attackSequence);
        reader.ReadValueSafe(out int attackTrigger);

        remoteFighter.ApplyRemoteState(
            new Vector3(x, y, z),
            Quaternion.Euler(0f, rotationY, 0f),
            health,
            defeated,
            moveSpeed,
            grounded,
            verticalVelocity,
            blocking,
            attackSequence,
            attackTrigger);
    }

    public void SendDamageToRemote(int amount)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        using FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp);
        writer.WriteValueSafe(amount);

        if (NetworkManager.Singleton.IsHost)
        {
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.ServerClientId)
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(DamageMessage, clientId, writer);
            }
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(DamageMessage, NetworkManager.ServerClientId, writer);
        }
    }

    public void Shutdown()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    private void OnDamageMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int amount);
        if (localFighter != null)
            localFighter.ApplyNetworkDamage(amount);
    }
}
