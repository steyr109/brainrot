using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class BrainrotDedicatedServer : MonoBehaviour
{
    private const string CharacterSelectionMessage = "BrainrotCharacterSelection";
    private const string MatchStartMessage = "BrainrotMatchStart";
    private const string StateMessage = "SahurPvpState";
    private const string DamageMessage = "SahurPvpDamage";

    private readonly List<ulong> waitingClients = new List<ulong>();
    private readonly Dictionary<ulong, string> selectedCharacters = new Dictionary<ulong, string>();
    private readonly Dictionary<ulong, ulong> opponents = new Dictionary<ulong, ulong>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BootIfServer()
    {
        if (!BrainrotNetworkConfig.IsDedicatedServer)
            return;

        GameObject serverObject = new GameObject("Brainrot Dedicated Server");
        DontDestroyOnLoad(serverObject);
        serverObject.AddComponent<BrainrotDedicatedServer>();
    }

    private void Awake()
    {
        NetworkManager manager = BrainrotNetworkConfig.EnsureNetworkManager(true);
        manager.OnClientDisconnectCallback += OnClientDisconnected;
        if (!manager.StartServer())
        {
            Debug.LogError("Brainrot dedicated server failed to start on port " + BrainrotNetworkConfig.ServerPort);
            return;
        }

        RegisterMessages();
        Debug.Log("Brainrot dedicated server started on port " + BrainrotNetworkConfig.ServerPort);
    }

    private void RegisterMessages()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(CharacterSelectionMessage, OnCharacterSelectionMessage);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnStateRelayMessage);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(DamageMessage, OnDamageRelayMessage);
    }

    private void OnCharacterSelectionMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out FixedString64Bytes characterValue);
        selectedCharacters[senderClientId] = characterValue.ToString();

        if (!waitingClients.Contains(senderClientId) && !opponents.ContainsKey(senderClientId))
            waitingClients.Add(senderClientId);

        TryCreateMatch();
    }

    private void TryCreateMatch()
    {
        while (waitingClients.Count >= 2)
        {
            ulong leftClient = waitingClients[0];
            ulong rightClient = waitingClients[1];
            waitingClients.RemoveAt(1);
            waitingClients.RemoveAt(0);

            if (!selectedCharacters.ContainsKey(leftClient) || !selectedCharacters.ContainsKey(rightClient))
                continue;

            opponents[leftClient] = rightClient;
            opponents[rightClient] = leftClient;
            SendMatchStart(leftClient, true, selectedCharacters[rightClient]);
            SendMatchStart(rightClient, false, selectedCharacters[leftClient]);
        }
    }

    private void SendMatchStart(ulong clientId, bool ownsLeft, string remoteCharacter)
    {
        FixedString64Bytes remoteValue = remoteCharacter;
        using FastBufferWriter writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteValueSafe(ownsLeft);
        writer.WriteValueSafe(remoteValue);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MatchStartMessage, clientId, writer);
    }

    private void OnStateRelayMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!opponents.TryGetValue(senderClientId, out ulong targetClientId))
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
        reader.ReadValueSafe(out float superCharge);

        using FastBufferWriter writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteValueSafe(x);
        writer.WriteValueSafe(y);
        writer.WriteValueSafe(z);
        writer.WriteValueSafe(rotationY);
        writer.WriteValueSafe(health);
        writer.WriteValueSafe(defeated);
        writer.WriteValueSafe(moveSpeed);
        writer.WriteValueSafe(grounded);
        writer.WriteValueSafe(verticalVelocity);
        writer.WriteValueSafe(blocking);
        writer.WriteValueSafe(attackSequence);
        writer.WriteValueSafe(attackTrigger);
        writer.WriteValueSafe(superCharge);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(StateMessage, targetClientId, writer);
    }

    private void OnDamageRelayMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!opponents.TryGetValue(senderClientId, out ulong targetClientId))
            return;

        reader.ReadValueSafe(out int amount);
        reader.ReadValueSafe(out bool hasKnockback);
        reader.ReadValueSafe(out float knockbackDirection);

        using FastBufferWriter writer = new FastBufferWriter(32, Allocator.Temp);
        writer.WriteValueSafe(amount);
        writer.WriteValueSafe(hasKnockback);
        writer.WriteValueSafe(knockbackDirection);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(DamageMessage, targetClientId, writer);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        waitingClients.Remove(clientId);
        selectedCharacters.Remove(clientId);

        if (opponents.TryGetValue(clientId, out ulong opponentId))
            opponents.Remove(opponentId);
        opponents.Remove(clientId);
    }
}
