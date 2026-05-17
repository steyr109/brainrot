using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    private const string ArenaModeKey = "BattleForBrainrot.Mode";
    private const string ArenaModeValue = "Sahur";
    private const string SelectedCharacterKey = "SelectedCharacter";
    private const string RemoteSelectedCharacterKey = "RemoteSelectedCharacter";
    private const string CharacterSelectionMessage = "BrainrotCharacterSelection";
    private const string GameModeKey = "BattleForBrainrot.GameMode";
    private const string GameModePve = "PVE";
    private const string GameModePvp = "PVP";
    private const ushort PvpPort = 7777;

    [SerializeField] private GameObject[] menuObjects;
    [SerializeField] private Button[] defaultButtons;
    [SerializeField] private AudioClip selectSound;
    [SerializeField] private AudioClip pressSound;

    private AudioSource audioSource;
    private Canvas menuCanvas;
    private GameObject characterSelectPanel;
    private GameObject gameModePanel;
    private GameObject matchmakingPanel;
    private Text gameModeLabel;
    private Text matchmakingStatusText;
    private GameObject showcaseSahur;
    private string selectedGameMode = GameModePve;
    private string localSelectedCharacter;
    private string remoteSelectedCharacter;
    private bool waitingForPvp;
    private bool characterSelectionMessageRegistered;

    private struct CharacterOption
    {
        public string displayName;
        public string resourceName;

        public CharacterOption(string displayName, string resourceName)
        {
            this.displayName = displayName;
            this.resourceName = resourceName;
        }
    }

    private static readonly CharacterOption[] Characters =
    {
        new CharacterOption("SAHUR", "Sahur"),
        new CharacterOption("BALERINA CAPUCHINO", "BalerinaCapuchino")
    };

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        selectedGameMode = PlayerPrefs.GetString(GameModeKey, GameModePve);
        PlayerPrefs.DeleteKey(ArenaModeKey);
        PlayerPrefs.DeleteKey(SelectedCharacterKey);
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);

        HideLegacyMenuObjects();
        HideLegacySceneUi();
        BuildMainMenu();
        SpawnShowcaseSahur();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            if (NetworkManager.Singleton.CustomMessagingManager != null)
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(CharacterSelectionMessage);
            characterSelectionMessageRegistered = false;
        }
    }

    private void Update()
    {
        if (!waitingForPvp || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        RegisterCharacterSelectionMessage();
        SendCharacterSelection();

        if (NetworkManager.Singleton.IsHost && NetworkManager.Singleton.ConnectedClientsIds.Count > 1)
            StartSelectedFight();
        else if (!NetworkManager.Singleton.IsHost && NetworkManager.Singleton.IsConnectedClient)
            StartSelectedFight();
    }

    public void OnPlayPressed()
    {
        PlaySound(pressSound);
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

        foreach (Canvas canvas in FindObjectsOfType<Canvas>())
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

        CreateTopCurrency(menuCanvas.transform, "ВАЛЮТА 2 0", new Vector2(-430f, -38f));
        CreateTopCurrency(menuCanvas.transform, "ВАЛЮТА 1 0", new Vector2(-180f, -38f));

        Button charactersButton = CreateMenuButton(menuCanvas.transform, "ПЕРСОНАЖИ", Anchor.BottomLeft, new Vector2(170f, 82f), new Vector2(220f, 78f), 27);
        charactersButton.onClick.AddListener(ShowCharacterSelect);

        Button playButton = CreateMenuButton(menuCanvas.transform, "ИГРАТЬ", Anchor.BottomRight, new Vector2(-165f, 82f), new Vector2(270f, 112f), 48);
        playButton.onClick.AddListener(OnPlayPressed);

        Button modeButton = CreateMenuButton(menuCanvas.transform, "РЕЖИМ\nИГРЫ", Anchor.Right, new Vector2(-86f, 30f), new Vector2(110f, 100f), 24);
        modeButton.onClick.AddListener(ShowGameModePanel);

        Button settingsButton = CreateMenuButton(menuCanvas.transform, "НАСТРОЙКИ", Anchor.TopRight, new Vector2(-83f, -64f), new Vector2(150f, 76f), 22);
        settingsButton.onClick.AddListener(() => PlaySound(pressSound));

        gameModeLabel = CreateText(menuCanvas.transform, "MODE: " + selectedGameMode, Anchor.Top, new Vector2(0f, -36f), new Vector2(260f, 36f), 24, TextAnchor.MiddleCenter, Color.red);
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

        float startX = -190f;
        for (int i = 0; i < Characters.Length; i++)
        {
            CharacterOption option = Characters[i];
            Button button = CreateMenuButton(window, option.displayName, Anchor.Center, new Vector2(startX + i * 380f, 20f), new Vector2(310f, 145f), 28);
            button.onClick.AddListener(() => SelectCharacter(option));
        }

        Button backButton = CreateMenuButton(window, "НАЗАД", Anchor.Center, new Vector2(0f, -160f), new Vector2(200f, 54f), 24);
        backButton.onClick.AddListener(() => characterSelectPanel.SetActive(false));
    }

    private void SelectCharacter(CharacterOption option)
    {
        PlaySound(pressSound);
        PlayerPrefs.SetString(ArenaModeKey, ArenaModeValue);
        PlayerPrefs.SetString(GameModeKey, selectedGameMode);
        localSelectedCharacter = option.resourceName;
        remoteSelectedCharacter = string.Empty;
        PlayerPrefs.SetString(SelectedCharacterKey, option.resourceName);
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        PlayerPrefs.Save();

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
            matchmakingPanel = CreateOverlayPanel("PVP Matchmaking Panel", new Vector2(650f, 310f));
            RectTransform window = matchmakingPanel.transform.GetChild(0).GetComponent<RectTransform>();
            CreateText(window, "ПОИСК ИГРОКОВ", Anchor.Center, new Vector2(0f, 85f), new Vector2(520f, 50f), 38, TextAnchor.MiddleCenter, Color.white);
            matchmakingStatusText = CreateText(window, "Ожидание соединения...", Anchor.Center, new Vector2(0f, 10f), new Vector2(540f, 42f), 26, TextAnchor.MiddleCenter, Color.red);
            Button cancelButton = CreateMenuButton(window, "ОТМЕНА", Anchor.Center, new Vector2(0f, -95f), new Vector2(190f, 54f), 24);
            cancelButton.onClick.AddListener(CancelMatchmaking);
        }

        matchmakingPanel.SetActive(true);
        StartPvpSearch();
    }

    private void StartPvpSearch()
    {
        waitingForPvp = true;
        EnsureNetworkManager();
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        bool isHost = Application.isEditor;
        if (matchmakingStatusText != null)
            matchmakingStatusText.text = isHost ? "Создан хост. Ждем второго игрока..." : "Подключаемся к хосту...";

        if (!NetworkManager.Singleton.IsListening)
        {
            if (isHost)
                NetworkManager.Singleton.StartHost();
            else
                NetworkManager.Singleton.StartClient();
        }

        RegisterCharacterSelectionMessage();
    }

    private static void EnsureNetworkManager()
    {
        if (NetworkManager.Singleton != null)
            return;

        GameObject managerObject = new GameObject("Local PVP Network Manager");
        DontDestroyOnLoad(managerObject);

        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", PvpPort, "0.0.0.0");

        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false
        };
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!waitingForPvp || NetworkManager.Singleton == null)
            return;

        RegisterCharacterSelectionMessage();
        SendCharacterSelection();

        if (!HasRemoteCharacter())
            return;

        if (NetworkManager.Singleton.IsHost && NetworkManager.Singleton.ConnectedClientsIds.Count > 1)
            StartSelectedFight();
        else if (!NetworkManager.Singleton.IsHost && NetworkManager.Singleton.IsConnectedClient)
            StartSelectedFight();
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
            ? PlayerPrefs.GetString(SelectedCharacterKey, "Sahur")
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

    private void OnCharacterSelectionMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out FixedString64Bytes characterValue);
        remoteSelectedCharacter = characterValue.ToString();
        PlayerPrefs.SetString(RemoteSelectedCharacterKey, remoteSelectedCharacter);
        PlayerPrefs.Save();

        if (matchmakingStatusText != null)
            matchmakingStatusText.text = "Игрок найден. Загружаем арену...";

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            SendCharacterSelection();

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
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.CustomMessagingManager != null)
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(CharacterSelectionMessage);
            characterSelectionMessageRegistered = false;

            if (NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
        }

        if (matchmakingPanel != null)
            matchmakingPanel.SetActive(false);
    }

    private void StartSelectedFight()
    {
        if (!waitingForPvp || !HasRemoteCharacter())
            return;

        waitingForPvp = false;
        if (string.IsNullOrEmpty(remoteSelectedCharacter))
            remoteSelectedCharacter = PlayerPrefs.GetString(RemoteSelectedCharacterKey, string.Empty);
        PlayerPrefs.SetString(RemoteSelectedCharacterKey, remoteSelectedCharacter);
        PlayerPrefs.Save();
        SceneManager.LoadScene("3DStage", LoadSceneMode.Single);
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
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
}






