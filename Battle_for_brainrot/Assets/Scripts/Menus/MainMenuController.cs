using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    private const string ArenaModeKey = "BattleForBrainrot.Mode";
    private const string ArenaModeValue = "Sahur";
    private const string TutorialModeKey = "BattleForBrainrot.TutorialMode";
    private const string TutorialCompletedKey = "BattleForBrainrot.TutorialCompleted";
    private const string TutorialVersionKey = "BattleForBrainrot.TutorialVersion";
    private const int CurrentTutorialVersion = 2;
    private const string SelectedCharacterKey = "SelectedCharacter";
    private const string RemoteSelectedCharacterKey = "RemoteSelectedCharacter";
    private const string CharacterSelectionMessage = "BrainrotCharacterSelection";
    private const string MatchStartMessage = "BrainrotMatchStart";
    private const string CryptaKey = "BattleForBrainrot.Crypta";
    private const string GameModeKey = "BattleForBrainrot.GameMode";
    private const string GameModePve = "PVE";
    private const string GameModePvp = "PVP";
    private const string InputModeKey = "BattleForBrainrot.InputMode";
    private const string InputModeKeyboard = "Keyboard";
    private const string InputModeJoystick = "Joystick";
    private const int LanDiscoveryPort = 47777;
    private const float LanHostFallbackMinSeconds = 1.4f;
    private const float LanHostFallbackMaxSeconds = 2.7f;
    private const float LanHostAnnounceInterval = 0.45f;
    private const string LanDiscoveryPrefix = "BRAINROT_PVP_LAN_HOST";

    [SerializeField] private GameObject[] menuObjects;
    [SerializeField] private Button[] defaultButtons;
    [SerializeField] private AudioClip selectSound;
    [SerializeField] private AudioClip pressSound;

    private AudioSource audioSource;
    private AudioClip fallbackPressClip;
    private Canvas menuCanvas;
    private GameObject characterSelectPanel;
    private GameObject gameModePanel;
    private GameObject matchmakingPanel;
    private Text gameModeLabel;
    private Text inputModeLabel;
    private Text matchmakingStatusText;
    private InputField lanAddressInput;
    private GameObject showcaseSahur;
    private string selectedGameMode = GameModePve;
    private string selectedInputMode = InputModeKeyboard;
    private string localSelectedCharacter;
    private string remoteSelectedCharacter;
    private bool waitingForPvp;
    private bool characterSelectionMessageRegistered;
    private bool lanAutoSearchActive;
    private bool lanAnnouncingHost;
    private Coroutine lanDiscoveryCoroutine;
    private UdpClient lanDiscoveryClient;
    private string lanDiscoveryId;

    private void Awake()
    {
        if (BrainrotNetworkConfig.IsDedicatedServer)
        {
            enabled = false;
            gameObject.SetActive(false);
            return;
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        lanDiscoveryId = System.Guid.NewGuid().ToString("N");
        selectedGameMode = PlayerPrefs.GetString(GameModeKey, GameModePve);
        selectedInputMode = PlayerPrefs.GetString(InputModeKey, GetDefaultInputMode());
        if (Application.isMobilePlatform)
        {
            selectedInputMode = InputModeJoystick;
            PlayerPrefs.SetString(InputModeKey, selectedInputMode);
            PlayerPrefs.Save();
        }

        PlayerPrefs.DeleteKey(ArenaModeKey);
        PlayerPrefs.DeleteKey(SelectedCharacterKey);
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        PlayerPrefs.DeleteKey(TutorialModeKey);

        HideLegacyMenuObjects();
        HideLegacySceneUi();
        BuildMainMenu();
        SpawnShowcaseSahur();
    }

    private void OnDestroy()
    {
        StopLanDiscovery();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            if (NetworkManager.Singleton.CustomMessagingManager != null)
            {
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(CharacterSelectionMessage);
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MatchStartMessage);
            }

            characterSelectionMessageRegistered = false;
        }
    }

    private void Update()
    {
        if (!waitingForPvp || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        RegisterCharacterSelectionMessage();
        if (NetworkManager.Singleton.IsConnectedClient)
            SendCharacterSelection();
    }

    public void OnPlayPressed()
    {
        PlaySound(pressSound);
        if (ShouldStartFirstLaunchTutorial())
            StartFirstLaunchTutorial();
        else
            ShowCharacterSelect();
    }

    public void OnMenuButtonPressed(int nextMenu)
    {
        // Kept for existing scene button bindings. Runtime menu handles navigation itself.
    }

    public void OnQuitPressed()
    {
        Application.Quit();
    }

    public void OnButtonPressed()
    {
        PlaySound(pressSound);
    }

    public void OnButtonSelected()
    {
        PlaySound(selectSound);
    }

    private static bool ShouldStartFirstLaunchTutorial()
    {
        return PlayerPrefs.GetInt(TutorialModeKey, 0) == 0 &&
               (PlayerPrefs.GetInt(TutorialCompletedKey, 0) == 0 ||
                PlayerPrefs.GetInt(TutorialVersionKey, 0) < CurrentTutorialVersion);
    }

    private static void StartFirstLaunchTutorial()
    {
        PlayerPrefs.SetInt(TutorialModeKey, 1);
        PlayerPrefs.SetString(ArenaModeKey, ArenaModeValue);
        PlayerPrefs.SetString(GameModeKey, GameModePve);
        PlayerPrefs.SetString(SelectedCharacterKey, BrainrotCharacterRegistry.Sahur);
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        PlayerPrefs.Save();
        SceneManager.LoadScene("3DStage", LoadSceneMode.Single);
    }

    private void HideLegacyMenuObjects()
    {
        if (menuObjects == null)
            return;

        foreach (GameObject menu in menuObjects)
        {
            if (menu == null)
                continue;

            SettingsMenu settingsMenu = menu.GetComponent<SettingsMenu>();
            if (settingsMenu != null)
                settingsMenu.LoadSettings();

            menu.SetActive(false);
        }
    }

    private void HideLegacySceneUi()
    {
        DisableLegacyObject("Main Menu");
        DisableLegacyObject("Background");
        DisableLegacyObject("Play Button");

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas == null || canvas.GetComponentInParent<MainMenuController>() != null)
                continue;

            canvas.gameObject.SetActive(false);
        }
    }

    private static void DisableLegacyObject(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        if (target != null)
            target.SetActive(false);
    }
    private void BuildMainMenu()
    {
        menuCanvas = new GameObject("Brainrot Main Menu Canvas").AddComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = menuCanvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        menuCanvas.gameObject.AddComponent<GraphicRaycaster>();
        BrainrotUiInput.EnsureEventSystem();

        CreateTopCurrency(menuCanvas.transform, "ВАЛЮТА 2 0", new Vector2(-430f, -38f));
        CreateTopCurrency(menuCanvas.transform, "КРИПТА " + PlayerPrefs.GetInt(CryptaKey, 0), new Vector2(-180f, -38f));

        Button charactersButton = CreateMenuButton(menuCanvas.transform, "ПЕРСОНАЖИ", Anchor.BottomLeft, new Vector2(170f, 82f), new Vector2(220f, 78f), 27);
        charactersButton.onClick.AddListener(ShowCharacterSelect);

        Button playButton = CreateMenuButton(menuCanvas.transform, "ИГРАТЬ", Anchor.BottomRight, new Vector2(-165f, 82f), new Vector2(270f, 112f), 48);
        playButton.onClick.AddListener(OnPlayPressed);

        Button modeButton = CreateMenuButton(menuCanvas.transform, "РЕЖИМ\nИГРЫ", Anchor.Right, new Vector2(-86f, 30f), new Vector2(110f, 100f), 24);
        modeButton.onClick.AddListener(ShowGameModePanel);

        Button settingsButton = CreateMenuButton(menuCanvas.transform, "НАСТРОЙКИ", Anchor.TopRight, new Vector2(-83f, -64f), new Vector2(150f, 76f), 22);
        settingsButton.onClick.AddListener(() => PlaySound(pressSound));

        gameModeLabel = CreateText(menuCanvas.transform, "MODE: " + selectedGameMode, Anchor.Top, new Vector2(0f, -36f), new Vector2(260f, 36f), 24, TextAnchor.MiddleCenter, Color.red);
        Button inputModeButton = CreateMenuButton(menuCanvas.transform, "УПРАВЛЕНИЕ", Anchor.Top, new Vector2(0f, -86f), new Vector2(250f, 46f), 19);
        inputModeButton.onClick.AddListener(ToggleInputMode);
        inputModeLabel = CreateText(menuCanvas.transform, GetInputModeLabel(), Anchor.Top, new Vector2(0f, -126f), new Vector2(300f, 30f), 19, TextAnchor.MiddleCenter, Color.red);
    }

    private void SpawnShowcaseSahur()
    {
        if (showcaseSahur != null)
            Destroy(showcaseSahur);

        GameObject prefab = Resources.Load<GameObject>("Sahur");
        if (prefab == null)
            return;

        showcaseSahur = Instantiate(prefab, new Vector3(0f, -0.6f, 0f), Quaternion.Euler(0f, 0f, 0f));
        showcaseSahur.name = "Menu Sahur Idle";
        showcaseSahur.transform.localScale = Vector3.one * 0.32f;

        foreach (NewFighter fighter in showcaseSahur.GetComponentsInChildren<NewFighter>(true))
            fighter.enabled = false;

        foreach (UnityEngine.InputSystem.PlayerInput input in showcaseSahur.GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true))
            input.enabled = false;

        foreach (SahurFighterController controller in showcaseSahur.GetComponentsInChildren<SahurFighterController>(true))
            controller.enabled = false;

        Animator animator = showcaseSahur.GetComponentInChildren<Animator>();
        if (animator != null)
            animator.Play("Idle", 0, 0f);

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.transform.position = new Vector3(0f, 1f, -4.6f);
            mainCamera.transform.rotation = Quaternion.Euler(3f, 0f, 0f);
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.white;
        }

        EnsureMenuLight("Menu Sahur Key Light", new Vector3(-2.5f, 4f, -3f), Quaternion.Euler(50f, 28f, 0f), 2.6f);
        EnsureMenuLight("Menu Sahur Fill Light", new Vector3(2f, 2.4f, -2f), Quaternion.Euler(35f, -35f, 0f), 1.1f);
    }

    private static void EnsureMenuLight(string objectName, Vector3 position, Quaternion rotation, float intensity)
    {
        GameObject lightObject = GameObject.Find(objectName);
        if (lightObject == null)
            lightObject = new GameObject(objectName);

        Light light = lightObject.GetComponent<Light>();
        if (light == null)
            light = lightObject.AddComponent<Light>();

        light.type = LightType.Directional;
        light.intensity = intensity;
        lightObject.transform.position = position;
        lightObject.transform.rotation = rotation;
    }

    private void ShowGameModePanel()
    {
        PlaySound(pressSound);
        if (gameModePanel != null)
        {
            gameModePanel.SetActive(true);
            return;
        }

        gameModePanel = CreateOverlayPanel("Game Mode Panel", new Vector2(520f, 300f));
        RectTransform window = gameModePanel.transform.GetChild(0).GetComponent<RectTransform>();
        CreateText(window, "РЕЖИМ ИГРЫ", Anchor.Center, new Vector2(0f, 98f), new Vector2(420f, 46f), 34, TextAnchor.MiddleCenter, Color.white);

        Button pveButton = CreateMenuButton(window, "PVE", Anchor.Center, new Vector2(-115f, 8f), new Vector2(170f, 70f), 34);
        pveButton.onClick.AddListener(() => SelectGameMode(GameModePve));

        Button pvpButton = CreateMenuButton(window, "PVP", Anchor.Center, new Vector2(115f, 8f), new Vector2(170f, 70f), 34);
        pvpButton.onClick.AddListener(() => SelectGameMode(GameModePvp));

        Button backButton = CreateMenuButton(window, "НАЗАД", Anchor.Center, new Vector2(0f, -105f), new Vector2(180f, 54f), 24);
        backButton.onClick.AddListener(() => gameModePanel.SetActive(false));
    }

    private void SelectGameMode(string mode)
    {
        selectedGameMode = mode;
        PlayerPrefs.SetString(GameModeKey, selectedGameMode);
        PlayerPrefs.Save();

        if (gameModeLabel != null)
            gameModeLabel.text = "MODE: " + selectedGameMode;

        if (gameModePanel != null)
            gameModePanel.SetActive(false);
    }

    private void ToggleInputMode()
    {
        PlaySound(pressSound);
        if (Application.isMobilePlatform)
        {
            selectedInputMode = InputModeJoystick;
            PlayerPrefs.SetString(InputModeKey, selectedInputMode);
            PlayerPrefs.Save();

            if (inputModeLabel != null)
                inputModeLabel.text = GetInputModeLabel();
            return;
        }

        selectedInputMode = selectedInputMode == InputModeKeyboard ? InputModeJoystick : InputModeKeyboard;
        PlayerPrefs.SetString(InputModeKey, selectedInputMode);
        PlayerPrefs.Save();

        if (inputModeLabel != null)
            inputModeLabel.text = GetInputModeLabel();
    }

    private string GetInputModeLabel()
    {
        return selectedInputMode == InputModeKeyboard ? "КЛАВИАТУРА" : "ДЖОЙСТИК";
    }

    private static string GetDefaultInputMode()
    {
        return Application.isMobilePlatform ? InputModeJoystick : InputModeKeyboard;
    }

    private void ShowCharacterSelect()
    {
        if (characterSelectPanel != null)
        {
            characterSelectPanel.SetActive(true);
            return;
        }

        characterSelectPanel = CreateOverlayPanel("Character Select Panel", new Vector2(790f, 440f));
        RectTransform window = characterSelectPanel.transform.GetChild(0).GetComponent<RectTransform>();
        CreateText(window, "ВЫБОР ПЕРСОНАЖА", Anchor.Center, new Vector2(0f, 160f), new Vector2(620f, 56f), 38, TextAnchor.MiddleCenter, Color.white);

        BrainrotCharacterDefinition[] characters = BrainrotCharacterRegistry.SelectableCharacters;
        float spacing = characters.Length > 2 ? 250f : 380f;
        float startX = -spacing * (characters.Length - 1) * 0.5f;
        Vector2 optionSize = characters.Length > 2 ? new Vector2(230f, 145f) : new Vector2(310f, 145f);
        for (int i = 0; i < characters.Length; i++)
        {
            BrainrotCharacterDefinition option = characters[i];
            Button button = CreateMenuButton(window, option.displayName, Anchor.Center, new Vector2(startX + i * spacing, 20f), optionSize, 24);
            button.onClick.AddListener(() => SelectCharacter(option));
        }

        Button backButton = CreateMenuButton(window, "НАЗАД", Anchor.Center, new Vector2(0f, -160f), new Vector2(200f, 54f), 24);
        backButton.onClick.AddListener(() => characterSelectPanel.SetActive(false));
    }

    private void SelectCharacter(BrainrotCharacterDefinition option)
    {
        PlaySound(pressSound);
        PlayerPrefs.SetString(ArenaModeKey, ArenaModeValue);
        PlayerPrefs.SetString(GameModeKey, selectedGameMode);
        localSelectedCharacter = option.resourceName;
        remoteSelectedCharacter = string.Empty;
        PlayerPrefs.SetString(SelectedCharacterKey, option.resourceName);
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        PlayerPrefs.Save();

        if (ShouldStartFirstLaunchTutorial())
        {
            StartFirstLaunchTutorial();
            return;
        }

        if (selectedGameMode == GameModePvp)
            ShowMatchmakingPanel();
        else
            SceneManager.LoadScene("3DStage", LoadSceneMode.Single);
    }

    private void ShowMatchmakingPanel()
    {
        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(false);

        if (matchmakingPanel == null)
        {
            matchmakingPanel = CreateOverlayPanel("PVP Wi-Fi Panel", new Vector2(720f, 430f));
            RectTransform window = matchmakingPanel.transform.GetChild(0).GetComponent<RectTransform>();
            CreateText(window, "PVP WI-FI", Anchor.Center, new Vector2(0f, 160f), new Vector2(520f, 50f), 38, TextAnchor.MiddleCenter, Color.white);
            CreateText(window, "IP ЭТОГО УСТРОЙСТВА: " + GetLocalIpAddressesText(), Anchor.Center, new Vector2(0f, 108f), new Vector2(660f, 42f), 18, TextAnchor.MiddleCenter, Color.red);

            Button searchButton = CreateMenuButton(window, "АВТО", Anchor.Center, new Vector2(-210f, 48f), new Vector2(150f, 64f), 26);
            searchButton.onClick.AddListener(StartLanAutoSearch);

            Button ipButton = CreateMenuButton(window, "ПО IP", Anchor.Center, new Vector2(0f, 48f), new Vector2(150f, 64f), 26);
            ipButton.onClick.AddListener(StartLanClient);

            Button serverButton = CreateMenuButton(window, "VPS", Anchor.Center, new Vector2(210f, 48f), new Vector2(150f, 64f), 26);
            serverButton.onClick.AddListener(StartOnlineServerClient);

            lanAddressInput = CreateInputField(window, "IP ХОСТА / VPS", BrainrotNetworkConfig.ServerAddress, Anchor.Center, new Vector2(0f, -30f), new Vector2(420f, 54f), 24);
            matchmakingStatusText = CreateText(window, "Для VPS введи IP сервера и нажми VPS. Для USB/LAN можно использовать АВТО или ПО IP.", Anchor.Center, new Vector2(0f, -100f), new Vector2(650f, 58f), 20, TextAnchor.MiddleCenter, Color.red);

            Button cancelButton = CreateMenuButton(window, "ОТМЕНА", Anchor.Center, new Vector2(0f, -165f), new Vector2(190f, 54f), 24);
            cancelButton.onClick.AddListener(CancelMatchmaking);
        }

        matchmakingPanel.SetActive(true);
        StartLanAutoSearch();
    }

    private void StartLanAutoSearch()
    {
        PlaySound(pressSound);
        StopLanDiscovery();
        ResetNetworkManager();

        waitingForPvp = true;
        lanAutoSearchActive = true;
        lanAnnouncingHost = false;
        remoteSelectedCharacter = string.Empty;
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        PlayerPrefs.SetString(GameModeKey, GameModePvp);
        PlayerPrefs.Save();

        EnsureNetworkManager();
        RegisterNetworkCallbacks();
        RegisterCharacterSelectionMessage();

        if (matchmakingStatusText != null)
            matchmakingStatusText.text = Application.isEditor
                ? "Editor ищет телефон-хост. На телефоне дождись хоста, затем при необходимости введи IP телефона тут."
                : "Ищем хоста в Wi-Fi сети...";

        lanDiscoveryCoroutine = StartCoroutine(LanAutoSearchLoop());
    }

    private IEnumerator LanAutoSearchLoop()
    {
        lanDiscoveryClient = CreateLanDiscoveryClient();
        float hostAt = Application.isEditor
            ? float.MaxValue
            : Time.realtimeSinceStartup + Random.Range(LanHostFallbackMinSeconds, LanHostFallbackMaxSeconds);
        float nextAnnounceAt = 0f;

        while (lanAutoSearchActive && waitingForPvp)
        {
            if (TryReadLanHost(out string hostAddress, out string hostId))
            {
                if (!lanAnnouncingHost || string.CompareOrdinal(hostId, lanDiscoveryId) < 0)
                {
                    ConnectToDiscoveredLanHost(hostAddress);
                    yield break;
                }
            }

            if (!lanAnnouncingHost && Time.realtimeSinceStartup >= hostAt)
                BecomeLanHost();

            if (lanAnnouncingHost && Time.realtimeSinceStartup >= nextAnnounceAt)
            {
                BroadcastLanHost();
                nextAnnounceAt = Time.realtimeSinceStartup + LanHostAnnounceInterval;
            }

            yield return null;
        }
    }

    private static UdpClient CreateLanDiscoveryClient()
    {
        UdpClient client = new UdpClient(AddressFamily.InterNetwork);
        client.EnableBroadcast = true;
        client.MulticastLoopback = false;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, LanDiscoveryPort));
        return client;
    }

    private bool TryReadLanHost(out string hostAddress, out string hostId)
    {
        hostAddress = string.Empty;
        hostId = string.Empty;
        if (lanDiscoveryClient == null)
            return false;

        while (lanDiscoveryClient.Available > 0)
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = lanDiscoveryClient.Receive(ref remote);
            string message = Encoding.UTF8.GetString(data);
            if (!message.StartsWith(LanDiscoveryPrefix))
                continue;

            string[] parts = message.Split('|');
            string discoveredId = parts.Length > 3 ? parts[3] : string.Empty;
            if (!string.IsNullOrEmpty(discoveredId) && discoveredId == lanDiscoveryId)
                continue;

            string remoteAddress = remote.Address.ToString();
            if (remoteAddress == GetLocalIpAddress())
                continue;

            hostAddress = remoteAddress;
            hostId = discoveredId;
            return true;
        }

        return false;
    }

    private void BecomeLanHost()
    {
        StartLanHost(false);
        lanAnnouncingHost = true;
        if (matchmakingStatusText != null)
            matchmakingStatusText.text = "Хост не найден. Создали бой, ждём второго игрока...";
    }

    private void ConnectToDiscoveredLanHost(string hostAddress)
    {
        StopLanDiscovery();
        ResetNetworkManager();
        StartLanClient(hostAddress, false);
        if (matchmakingStatusText != null)
            matchmakingStatusText.text = "Хост найден: " + hostAddress + ". Подключаемся...";
    }

    private void BroadcastLanHost()
    {
        if (lanDiscoveryClient == null)
            return;

        string character = string.IsNullOrEmpty(localSelectedCharacter)
            ? PlayerPrefs.GetString(SelectedCharacterKey, BrainrotCharacterRegistry.Sahur)
            : localSelectedCharacter;
        string message = LanDiscoveryPrefix + "|" + BrainrotNetworkConfig.ServerPort + "|" + character + "|" + lanDiscoveryId;
        byte[] data = Encoding.UTF8.GetBytes(message);
        foreach (IPAddress address in GetLanBroadcastAddresses())
            lanDiscoveryClient.Send(data, data.Length, new IPEndPoint(address, LanDiscoveryPort));
    }

    private static IEnumerable<IPAddress> GetLanBroadcastAddresses()
    {
        yield return IPAddress.Broadcast;

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicast.IPv4Mask == null ||
                    IPAddress.IsLoopback(unicast.Address))
                {
                    continue;
                }

                byte[] addressBytes = unicast.Address.GetAddressBytes();
                byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();
                byte[] broadcastBytes = new byte[addressBytes.Length];
                for (int i = 0; i < broadcastBytes.Length; i++)
                    broadcastBytes[i] = (byte)(addressBytes[i] | ~maskBytes[i]);

                yield return new IPAddress(broadcastBytes);
            }
        }
    }

    private void StartLanHost()
    {
        StartLanHost(true);
    }

    private void StartLanHost(bool resetDiscovery)
    {
        PlaySound(pressSound);
        if (resetDiscovery)
        {
            StopLanDiscovery();
            ResetNetworkManager();
        }

        waitingForPvp = true;
        lanAutoSearchActive = true;
        remoteSelectedCharacter = string.Empty;
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        PlayerPrefs.SetInt(BrainrotNetworkConfig.PvpOwnsLeftKey, 1);
        PlayerPrefs.SetString(GameModeKey, GameModePvp);
        PlayerPrefs.Save();

        EnsureNetworkManager();
        BrainrotNetworkConfig.UseLanHost();
        RegisterNetworkCallbacks();
        RegisterCharacterSelectionMessage();

        if (!NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.StartHost())
        {
            if (matchmakingStatusText != null)
                matchmakingStatusText.text = "Не удалось создать хост. Проверь порт 7777 и Firewall.";
            return;
        }

        if (matchmakingStatusText != null)
            matchmakingStatusText.text = "Игра создана. Второй игрок должен подключиться к IP: " + GetLocalIpAddress();
    }

    private void StartLanClient()
    {
        string address = lanAddressInput != null ? lanAddressInput.text : BrainrotNetworkConfig.LanAddress;
        StartLanClient(address, true);
    }

    private void StartLanClient(string address, bool resetDiscovery)
    {
        PlaySound(pressSound);
        if (resetDiscovery || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening))
        {
            StopLanDiscovery();
            ResetNetworkManager();
        }

        waitingForPvp = true;
        lanAutoSearchActive = false;
        remoteSelectedCharacter = string.Empty;
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        PlayerPrefs.SetInt(BrainrotNetworkConfig.PvpOwnsLeftKey, 0);
        PlayerPrefs.SetString(GameModeKey, GameModePvp);
        PlayerPrefs.Save();

        EnsureNetworkManager();
        BrainrotNetworkConfig.UseLanClient(address);
        RegisterNetworkCallbacks();
        RegisterCharacterSelectionMessage();

        if (!NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.StartClient())
        {
            if (matchmakingStatusText != null)
                matchmakingStatusText.text = "Не удалось начать подключение к " + BrainrotNetworkConfig.LanAddress;
            return;
        }

        if (matchmakingStatusText != null)
            matchmakingStatusText.text = "Подключаемся к " + BrainrotNetworkConfig.LanAddress + ":" + BrainrotNetworkConfig.ServerPort + "...";
    }

    private void StartPvpSearch()
    {
        waitingForPvp = true;
        EnsureNetworkManager();
        BrainrotNetworkConfig.UseOnlineClient();
        RegisterNetworkCallbacks();

        if (matchmakingStatusText != null)
            matchmakingStatusText.text = "Подключаемся к серверу " + BrainrotNetworkConfig.ServerAddress + ":" + BrainrotNetworkConfig.ServerPort + "...";

        if (!NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.StartClient();

        RegisterCharacterSelectionMessage();
    }

    private void StartOnlineServerClient()
    {
        PlaySound(pressSound);
        string address = lanAddressInput != null ? lanAddressInput.text : BrainrotNetworkConfig.ServerAddress;
        StopLanDiscovery();
        ResetNetworkManager();

        waitingForPvp = true;
        remoteSelectedCharacter = string.Empty;
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        PlayerPrefs.SetString(GameModeKey, GameModePvp);
        PlayerPrefs.Save();

        EnsureNetworkManager();
        BrainrotNetworkConfig.UseOnlineClient(address);
        RegisterNetworkCallbacks();
        RegisterCharacterSelectionMessage();

        if (!NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.StartClient())
        {
            if (matchmakingStatusText != null)
                matchmakingStatusText.text = "Не удалось начать подключение к VPS " + BrainrotNetworkConfig.ServerAddress;
            return;
        }

        if (matchmakingStatusText != null)
            matchmakingStatusText.text = "Подключаемся к VPS " + BrainrotNetworkConfig.ServerAddress + ":" + BrainrotNetworkConfig.ServerPort + "...";
    }

    private static void EnsureNetworkManager()
    {
        BrainrotNetworkConfig.EnsureNetworkManager(false);
    }

    private void RegisterNetworkCallbacks()
    {
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!waitingForPvp || NetworkManager.Singleton == null)
            return;

        RegisterCharacterSelectionMessage();

        if (NetworkManager.Singleton.IsHost)
        {
            if (clientId != NetworkManager.ServerClientId)
                SendCharacterSelectionTo(clientId);
        }
        else
        {
            if (matchmakingStatusText != null)
                matchmakingStatusText.text = "Соединение установлено. Отправляем персонажа...";
            SendCharacterSelection();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!waitingForPvp || NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsHost && matchmakingStatusText != null)
            matchmakingStatusText.text = "Подключение не удалось или было разорвано. Проверь IP и попробуй ПО IP.";
    }

    private void RegisterCharacterSelectionMessage()
    {
        if (characterSelectionMessageRegistered ||
            NetworkManager.Singleton == null ||
            NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(CharacterSelectionMessage);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(CharacterSelectionMessage, OnCharacterSelectionMessage);
        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MatchStartMessage);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(MatchStartMessage, OnMatchStartMessage);
        characterSelectionMessageRegistered = true;
    }

    private void SendCharacterSelection()
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsListening ||
            NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        string character = string.IsNullOrEmpty(localSelectedCharacter)
            ? PlayerPrefs.GetString(SelectedCharacterKey, BrainrotCharacterRegistry.Sahur)
            : localSelectedCharacter;

        FixedString64Bytes characterValue = character;
        using FastBufferWriter writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteValueSafe(characterValue);

        if (NetworkManager.Singleton.IsHost)
        {
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.ServerClientId)
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(CharacterSelectionMessage, clientId, writer);
            }
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(CharacterSelectionMessage, NetworkManager.ServerClientId, writer);
        }
    }

    private void SendCharacterSelectionTo(ulong clientId)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsListening ||
            NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        string character = string.IsNullOrEmpty(localSelectedCharacter)
            ? PlayerPrefs.GetString(SelectedCharacterKey, BrainrotCharacterRegistry.Sahur)
            : localSelectedCharacter;

        FixedString64Bytes characterValue = character;
        using FastBufferWriter writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteValueSafe(characterValue);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(CharacterSelectionMessage, clientId, writer);
    }

    private void OnCharacterSelectionMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out FixedString64Bytes characterValue);
        remoteSelectedCharacter = characterValue.ToString();
        PlayerPrefs.SetString(RemoteSelectedCharacterKey, remoteSelectedCharacter);
        PlayerPrefs.Save();

        if (BrainrotNetworkConfig.IsLanMode)
        {
            if (matchmakingStatusText != null)
                matchmakingStatusText.text = "Игрок найден. Загружаем арену...";

            StartSelectedFight();
        }
    }

    private void OnMatchStartMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out bool ownsLeft);
        reader.ReadValueSafe(out FixedString64Bytes remoteCharacterValue);

        remoteSelectedCharacter = remoteCharacterValue.ToString();
        PlayerPrefs.SetInt(BrainrotNetworkConfig.PvpOwnsLeftKey, ownsLeft ? 1 : 0);
        PlayerPrefs.SetString(RemoteSelectedCharacterKey, remoteSelectedCharacter);
        PlayerPrefs.SetString(GameModeKey, GameModePvp);
        PlayerPrefs.Save();

        if (matchmakingStatusText != null)
            matchmakingStatusText.text = "Игрок найден. Загружаем арену...";

        StartSelectedFight();
    }
    private bool HasRemoteCharacter()
    {
        return !string.IsNullOrEmpty(remoteSelectedCharacter) ||
               !string.IsNullOrEmpty(PlayerPrefs.GetString(RemoteSelectedCharacterKey, string.Empty));
    }
    private void CancelMatchmaking()
    {
        waitingForPvp = false;
        StopLanDiscovery();
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.CustomMessagingManager != null)
            {
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(CharacterSelectionMessage);
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MatchStartMessage);
            }

            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

            characterSelectionMessageRegistered = false;

            if (NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
        }

        if (matchmakingPanel != null)
            matchmakingPanel.SetActive(false);
    }

    private void ResetNetworkManager()
    {
        characterSelectionMessageRegistered = false;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    private void StartSelectedFight()
    {
        if (!waitingForPvp || !HasRemoteCharacter())
            return;

        waitingForPvp = false;
        StopLanDiscovery();
        if (string.IsNullOrEmpty(remoteSelectedCharacter))
            remoteSelectedCharacter = PlayerPrefs.GetString(RemoteSelectedCharacterKey, string.Empty);
        PlayerPrefs.SetString(RemoteSelectedCharacterKey, remoteSelectedCharacter);
        PlayerPrefs.Save();
        SceneManager.LoadScene("3DStage", LoadSceneMode.Single);
    }

    private void StopLanDiscovery()
    {
        lanAutoSearchActive = false;
        lanAnnouncingHost = false;
        StopLanDiscoveryCoroutineOnly();

        if (lanDiscoveryClient != null)
        {
            lanDiscoveryClient.Close();
            lanDiscoveryClient = null;
        }
    }

    private void StopLanDiscoveryCoroutineOnly()
    {
        if (lanDiscoveryCoroutine != null)
        {
            StopCoroutine(lanDiscoveryCoroutine);
            lanDiscoveryCoroutine = null;
        }
    }

    private GameObject CreateOverlayPanel(string name, Vector2 windowSize)
    {
        GameObject panel = new GameObject(name);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.SetParent(menuCanvas.transform, false);
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image overlay = panel.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.72f);

        CreatePanel(panelRect, "Window", Anchor.Center, Vector2.zero, windowSize, new Color(0.06f, 0.06f, 0.06f, 0.96f));
        return panel;
    }

    private enum Anchor
    {
        Center,
        Top,
        TopRight,
        Right,
        BottomLeft,
        BottomRight
    }

    private static Button CreateMenuButton(Transform parent, string label, Anchor anchor, Vector2 position, Vector2 size, int fontSize)
    {
        RectTransform rect = CreatePanel(parent, label + " Button", anchor, position, size, new Color(0.86f, 0.86f, 0.86f, 1f));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();

        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;

        Text text = CreateText(rect, label, Anchor.Center, Vector2.zero, size, fontSize, TextAnchor.MiddleCenter, Color.red);
        text.fontStyle = FontStyle.Bold;
        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(3f, -3f);
        return button;
    }

    private static RectTransform CreatePanel(Transform parent, string name, Anchor anchor, Vector2 position, Vector2 size, Color color)
    {
        GameObject panel = new GameObject(name);
        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        ApplyAnchor(rect, anchor);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = panel.AddComponent<Image>();
        image.color = color;
        return rect;
    }

    private static void CreateTopCurrency(Transform parent, string value, Vector2 position)
    {
        RectTransform root = CreatePanel(parent, value + " Panel", Anchor.TopRight, position, new Vector2(200f, 34f), new Color(0.86f, 0.86f, 0.86f, 1f));
        CreateText(root, value, Anchor.Center, Vector2.zero, new Vector2(190f, 32f), 21, TextAnchor.MiddleCenter, Color.red);
    }

    private static InputField CreateInputField(Transform parent, string placeholderValue, string value, Anchor anchor, Vector2 position, Vector2 size, int fontSize)
    {
        RectTransform rect = CreatePanel(parent, placeholderValue + " Input", anchor, position, size, Color.white);
        InputField input = rect.gameObject.AddComponent<InputField>();
        input.text = value;
        input.textComponent = CreateText(rect, value, Anchor.Center, new Vector2(8f, 0f), new Vector2(size.x - 24f, size.y), fontSize, TextAnchor.MiddleLeft, Color.black);
        Text placeholder = CreateText(rect, placeholderValue, Anchor.Center, new Vector2(8f, 0f), new Vector2(size.x - 24f, size.y), fontSize, TextAnchor.MiddleLeft, new Color(0.45f, 0.45f, 0.45f, 0.8f));
        input.placeholder = placeholder;
        input.contentType = InputField.ContentType.Standard;
        input.characterLimit = 64;
        return input;
    }

    private static Text CreateText(Transform parent, string value, Anchor anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color)
    {
        GameObject textObject = new GameObject("Text");
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        ApplyAnchor(rect, anchor);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null)
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        return text;
    }

    private static void ApplyAnchor(RectTransform rect, Anchor anchor)
    {
        Vector2 value;
        switch (anchor)
        {
            case Anchor.Top:
                value = new Vector2(0.5f, 1f);
                break;
            case Anchor.TopRight:
                value = new Vector2(1f, 1f);
                break;
            case Anchor.Right:
                value = new Vector2(1f, 0.5f);
                break;
            case Anchor.BottomLeft:
                value = new Vector2(0f, 0f);
                break;
            case Anchor.BottomRight:
                value = new Vector2(1f, 0f);
                break;
            default:
                value = new Vector2(0.5f, 0.5f);
                break;
        }

        rect.anchorMin = value;
        rect.anchorMax = value;
        rect.pivot = value;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource == null)
            return;

        if (clip == null)
        {
            if (fallbackPressClip == null)
                fallbackPressClip = Resources.Load<AudioClip>("Audio/UI_Sounds/Button_Click_1");
            clip = fallbackPressClip;
        }

        if (clip != null)
            audioSource.PlayOneShot(clip);
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                IPInterfaceProperties properties = networkInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    IPAddress address = unicast.Address;
                    if (address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address) &&
                        !address.ToString().StartsWith("169.254."))
                    {
                        return address.ToString();
                    }
                }
            }

            IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress address in hostEntry.AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    return address.ToString();
            }
        }
        catch
        {
        }

        return "127.0.0.1";
    }

    private static string GetLocalIpAddressesText()
    {
        List<string> addresses = new List<string>();
        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                IPInterfaceProperties properties = networkInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    IPAddress address = unicast.Address;
                    string value = address.ToString();
                    if (address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address) &&
                        !value.StartsWith("169.254.") &&
                        !addresses.Contains(value))
                    {
                        addresses.Add(value);
                    }
                }
            }
        }
        catch
        {
        }

        if (addresses.Count == 0)
            return GetLocalIpAddress();

        return string.Join(" / ", addresses);
    }
}









