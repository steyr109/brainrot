using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SahurArenaBootstrap : MonoBehaviour
{
    private const string ArenaModeKey = "BattleForBrainrot.Mode";
    private const string ArenaModeValue = "Sahur";
    private const string TutorialModeKey = "BattleForBrainrot.TutorialMode";
    private const string TutorialCompletedKey = "BattleForBrainrot.TutorialCompleted";
    private const string TutorialVersionKey = "BattleForBrainrot.TutorialVersion";
    private const int CurrentTutorialVersion = 2;
    private const string SelectedCharacterKey = "SelectedCharacter";
    private const string RemoteSelectedCharacterKey = "RemoteSelectedCharacter";
    private const string CryptaKey = "BattleForBrainrot.Crypta";
    private const string GameModeKey = "BattleForBrainrot.GameMode";
    private const string GameModePvp = "PVP";
    private const string InputModeKey = "BattleForBrainrot.InputMode";
    private const string InputModeKeyboard = "Keyboard";
    private const float RoundDuration = 60f;
    private const int VictoryReward = 50;
    private const int TutorialVictoryReward = 15;
    private static readonly string[] ArenaSceneNames = { "Map1", "Map2" };
    private const float ArenaFightGroundY = -0.55f;
    private const float ArenaFightPlaneZ = 0f;
    private static readonly Vector3 ArenaCameraPosition = new Vector3(0f, 1.58f, -6.85f);
    private static readonly Quaternion ArenaCameraRotation = Quaternion.Euler(5.2f, 0f, 0f);
    private const float ArenaCameraFov = 45f;
    private const float LoadedArenaSourceGroundY = -1.04f;
    private const float LoadedArenaSourcePlaneZ = 6.65f;

    private HealthBarView playerHealthBar;
    private HealthBarView enemyHealthBar;
    private SuperMeterView playerSuperMeter;
    private SuperMeterView enemySuperMeter;
    private Text timerText;
    private RectTransform ownedMarkerRoot;
    private GameObject resultPanel;
    private Text resultTitleText;
    private Text resultDetailsText;
    private Text tutorialHintText;
    private Button superAttackButton;
    private Text superAttackButtonText;
    private SahurFighterController ownedFighter;
    private SahurPvpNetworkController pvpNetwork;
    private BrainrotAudioEvents audioEvents;
    private SahurFighterController player;
    private SahurFighterController enemy;
    private BrainrotCharacterDefinition leftFighterDefinition;
    private BrainrotCharacterDefinition rightFighterDefinition;
    private float remainingTime = RoundDuration;
    private bool fightEnded;
    private int pendingReward;
    private bool tutorialModeActive;
    private bool tutorialMoved;
    private bool tutorialJumped;
    private bool tutorialAttacked;
    private bool tutorialSuperSeenReady;
    private float tutorialStartX;
    private string loadedArenaSceneName;
    private static readonly System.Collections.Generic.Dictionary<string, Material> arenaMaterialCache = new System.Collections.Generic.Dictionary<string, Material>();

    private class HealthBarView
    {
        public RectTransform fillRect;
        public Text valueText;
        public bool drainsFromRight;
    }

    private class SuperMeterView
    {
        public RectTransform fillRect;
        public Text valueText;
        public bool fillsFromRight;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "3DStage")
            return;

        bool hasSelectedCharacter = !string.IsNullOrEmpty(PlayerPrefs.GetString(SelectedCharacterKey, string.Empty));
        bool tutorialMode = PlayerPrefs.GetInt(TutorialModeKey, 0) == 1;
        bool hasArenaRequest = PlayerPrefs.GetString(ArenaModeKey) == ArenaModeValue || hasSelectedCharacter || tutorialMode;
        if (!hasArenaRequest)
        {
            SceneManager.LoadScene("Menu", LoadSceneMode.Single);
            return;
        }

        var existing = FindFirstObjectByType<SahurArenaBootstrap>();
        if (existing == null)
            new GameObject("Brainrot Arena Bootstrap").AddComponent<SahurArenaBootstrap>();
    }

    private void Start()
    {
        Time.timeScale = 1f;
        tutorialModeActive = PlayerPrefs.GetInt(TutorialModeKey, 0) == 1;
        ClearLegacyFightScene();
        LoadRandomArenaScene();
        audioEvents = BrainrotAudioEvents.Ensure();
        audioEvents.PlayFightStart();
        SpawnFighters();
        if (tutorialModeActive && player != null)
            tutorialStartX = player.transform.position.x;
        BuildFightUi();
        UpdateHealthUi();
        UpdateOwnedMarker();
    }

    private void Update()
    {
        if (fightEnded)
            return;

        UpdateHealthUi();
        UpdateSuperAttackButton();
        UpdateOwnedMarker();
        UpdateTutorialProgress();
        UpdateTimer();
        CheckFightEnd();
    }

    private static BrainrotCharacterDefinition GetSelectedFighter()
    {
        return BrainrotCharacterRegistry.GetByResourceName(PlayerPrefs.GetString(SelectedCharacterKey, BrainrotCharacterRegistry.Sahur));
    }

    private static BrainrotCharacterDefinition GetRemoteFighter()
    {
        return BrainrotCharacterRegistry.GetByResourceName(PlayerPrefs.GetString(RemoteSelectedCharacterKey, BrainrotCharacterRegistry.Sahur));
    }

    private void ClearLegacyFightScene()
    {
        var manager = FindFirstObjectByType<FightManager>();
        if (manager != null)
            manager.gameObject.SetActive(false);

        DisableSceneObject("GUI");
        DisableSceneObject("Rematch Canvas");
        DisableSceneObject("Round Animator");
        ClearLegacyArenaObjects();

        foreach (NewFighter legacyFighter in FindObjectsByType<NewFighter>(FindObjectsSortMode.None))
            Destroy(legacyFighter.gameObject);

        foreach (Projectile projectile in FindObjectsByType<Projectile>(FindObjectsSortMode.None))
            Destroy(projectile.gameObject);
    }


    private static void ClearLegacyArenaObjects()
    {
        string[] legacyObjectNames =
        {
            "TheGrid",
            "Ground",
            "Left Wall",
            "Right Wall",
            "Main Camera",
            "Front Camera",
            "Back Camera",
            "Particle Camera",
            "Directional Light",
            "Post Processing Volume"
        };

        for (int i = 0; i < legacyObjectNames.Length; i++)
            DisableSceneObject(legacyObjectNames[i]);
    }
    private static void DisableSceneObject(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        if (target != null)
            target.SetActive(false);
    }

    private void SpawnFighters()
    {
        bool tutorialMode = PlayerPrefs.GetInt(TutorialModeKey, 0) == 1;
        bool pvpMode = PlayerPrefs.GetString(GameModeKey, "PVE") == GameModePvp;
        bool hostOwnsLeft = PlayerPrefs.GetInt(BrainrotNetworkConfig.PvpOwnsLeftKey, 1) == 1;

        BrainrotCharacterDefinition localDefinition = GetSelectedFighter();
        BrainrotCharacterDefinition remoteDefinition = tutorialMode ? BrainrotCharacterRegistry.TutorialShoto : pvpMode ? GetRemoteFighter() : localDefinition;
        leftFighterDefinition = !pvpMode || hostOwnsLeft ? localDefinition : remoteDefinition;
        rightFighterDefinition = !pvpMode || hostOwnsLeft ? remoteDefinition : localDefinition;

        GameObject leftPrefab = Resources.Load<GameObject>(leftFighterDefinition.resourceName);
        GameObject rightPrefab = Resources.Load<GameObject>(rightFighterDefinition.resourceName);
        if (leftPrefab == null || rightPrefab == null)
        {
            Debug.LogError("Selected fighter prefab was not found. Left: " + leftFighterDefinition.resourceName + ", Right: " + rightFighterDefinition.resourceName);
            return;
        }

        float rightSpawnX = tutorialMode ? -1.15f : 2.6f;
        float leftGroundY = leftFighterDefinition.spawnY;
        float rightGroundY = rightFighterDefinition.spawnY;
        GameObject playerObject = Instantiate(leftPrefab, new Vector3(-2.6f, leftGroundY, ArenaFightPlaneZ), Quaternion.identity);
        GameObject enemyObject = Instantiate(rightPrefab, new Vector3(rightSpawnX, rightGroundY, ArenaFightPlaneZ), Quaternion.identity);

        playerObject.name = leftFighterDefinition.displayName + " Player";
        enemyObject.name = rightFighterDefinition.displayName + " Opponent";
        playerObject.transform.localScale = Vector3.one * leftFighterDefinition.scale;
        enemyObject.transform.localScale = Vector3.one * rightFighterDefinition.scale;
        NormalizeFighterVisual(playerObject, leftGroundY);
        NormalizeFighterVisual(enemyObject, rightGroundY);
        EnsureFighterVisible(playerObject);
        EnsureFighterVisible(enemyObject);

        player = PrepareFighter(playerObject);
        enemy = PrepareFighter(enemyObject);

        bool playerIsLocal = !pvpMode || hostOwnsLeft;
        bool enemyIsLocal = pvpMode && !hostOwnsLeft;

        if (tutorialMode)
            enemy.SetBasicAttackOnlyAi(true);

        player.Initialize(playerIsLocal, enemy, 100, leftGroundY);
        enemy.Initialize(enemyIsLocal, player, 100, rightGroundY);
        player.SetOpponent(enemy);
        enemy.SetOpponent(player);

        if (pvpMode)
        {
            player.SetAiEnabled(false);
            enemy.SetAiEnabled(false);
            ownedFighter = hostOwnsLeft ? player : enemy;
            SahurFighterController remoteFighter = hostOwnsLeft ? enemy : player;
            pvpNetwork = gameObject.AddComponent<SahurPvpNetworkController>();
            pvpNetwork.Initialize(ownedFighter, remoteFighter);
            ownedFighter.SetPvpNetwork(pvpNetwork);
        }
        else
        {
            ownedFighter = player;
        }
    }

    private static void NormalizeFighterVisual(GameObject fighterObject, float groundY)
    {
        Bounds bounds = GetRendererBounds(fighterObject);
        if (bounds.size == Vector3.zero)
            return;

        Vector3 delta = new Vector3(0f, groundY - bounds.min.y, -bounds.center.z);
        for (int i = 0; i < fighterObject.transform.childCount; i++)
            fighterObject.transform.GetChild(i).position += delta;
    }

    private static void EnsureFighterVisible(GameObject fighterObject)
    {
        foreach (Renderer renderer in fighterObject.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    private static Bounds GetRendererBounds(GameObject fighterObject)
    {
        Renderer[] renderers = fighterObject.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = new Bounds(fighterObject.transform.position, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds ? bounds : new Bounds(fighterObject.transform.position, Vector3.zero);
    }

    private SahurFighterController PrepareFighter(GameObject fighterObject)
    {
        foreach (NewFighter oldFighter in fighterObject.GetComponentsInChildren<NewFighter>(true))
            oldFighter.enabled = false;

        foreach (UnityEngine.InputSystem.PlayerInput oldInput in fighterObject.GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true))
            oldInput.enabled = false;

        var controller = fighterObject.GetComponent<SahurFighterController>();
        if (controller == null)
            controller = fighterObject.AddComponent<SahurFighterController>();

        return controller;
    }

    private void BuildFightUi()
    {
        Canvas canvas = new GameObject("Brainrot Fight UI").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvas.gameObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        playerHealthBar = CreateCornerHealthBar(canvas.transform, leftFighterDefinition.displayName, new Vector2(80f, -46f), true, new Color(0.12f, 0.82f, 0.35f, 1f));
        enemyHealthBar = CreateCornerHealthBar(canvas.transform, rightFighterDefinition.displayName, new Vector2(-80f, -46f), false, new Color(0.9f, 0.16f, 0.18f, 1f));
        playerSuperMeter = CreateSuperMeter(canvas.transform, new Vector2(80f, -112f), true);
        enemySuperMeter = CreateSuperMeter(canvas.transform, new Vector2(-80f, -112f), false);
        timerText = CreateTimerText(canvas.transform);
        CreateOwnedMarker(canvas.transform, out ownedMarkerRoot);
        CreateResultPanel(canvas.transform);
        if (tutorialModeActive)
            tutorialHintText = CreateTutorialHint(canvas.transform);

        if (UsesTouchControls())
            CreateJoystickControls(canvas.transform);
    }

    private void UpdateHealthUi()
    {
        UpdateHealthBar(playerHealthBar, player);
        UpdateHealthBar(enemyHealthBar, enemy);
        UpdateSuperMeter(playerSuperMeter, player);
        UpdateSuperMeter(enemySuperMeter, enemy);
    }

    private void UpdateOwnedMarker()
    {
        if (ownedMarkerRoot == null || ownedFighter == null || Camera.main == null)
            return;

        Vector3 worldPosition = ownedFighter.transform.position + Vector3.down * 0.25f;
        Vector2 screenPosition = Camera.main.WorldToScreenPoint(worldPosition);
        RectTransform canvasRect = ownedMarkerRoot.parent as RectTransform;
        if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 localPoint))
            ownedMarkerRoot.anchoredPosition = localPoint;
    }

    private static void UpdateHealthBar(HealthBarView healthBar, SahurFighterController fighter)
    {
        if (healthBar == null || healthBar.fillRect == null || fighter == null)
            return;

        float health01 = Mathf.Clamp01((float)fighter.CurrentHealth / fighter.MaxHealth);
        healthBar.fillRect.gameObject.SetActive(health01 > 0.001f);
        Vector2 anchorMin = healthBar.fillRect.anchorMin;
        Vector2 anchorMax = healthBar.fillRect.anchorMax;

        if (healthBar.drainsFromRight)
        {
            anchorMin.x = 1f - health01;
            anchorMax.x = 1f;
        }
        else
        {
            anchorMin.x = 0f;
            anchorMax.x = health01;
        }

        healthBar.fillRect.anchorMin = anchorMin;
        healthBar.fillRect.anchorMax = anchorMax;

        if (healthBar.valueText != null)
            healthBar.valueText.text = fighter.CurrentHealth + " / " + fighter.MaxHealth;
    }

    private static void UpdateSuperMeter(SuperMeterView meter, SahurFighterController fighter)
    {
        if (meter == null || meter.fillRect == null || fighter == null)
            return;

        float charge01 = fighter.SuperChargeNormalized;
        meter.fillRect.gameObject.SetActive(charge01 > 0.001f);
        Vector2 anchorMin = meter.fillRect.anchorMin;
        Vector2 anchorMax = meter.fillRect.anchorMax;

        if (meter.fillsFromRight)
        {
            anchorMin.x = 1f - charge01;
            anchorMax.x = 1f;
        }
        else
        {
            anchorMin.x = 0f;
            anchorMax.x = charge01;
        }

        meter.fillRect.anchorMin = anchorMin;
        meter.fillRect.anchorMax = anchorMax;

        if (meter.valueText != null)
            meter.valueText.text = charge01 >= 1f ? "SUPER READY" : "SUPER " + Mathf.FloorToInt(charge01 * 100f) + "%";
    }

    private void UpdateTimer()
    {
        remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime);
        if (timerText != null)
            timerText.text = Mathf.CeilToInt(remainingTime).ToString();
    }

    private void CheckFightEnd()
    {
        if (player == null || enemy == null)
            return;

        if (player.IsDefeated || enemy.IsDefeated || remainingTime <= 0f)
            ShowFightResult();
    }

    private void ShowFightResult()
    {
        fightEnded = true;
        if (player != null)
            player.SetInputEnabled(false);
        if (enemy != null)
            enemy.SetInputEnabled(false);

        if (resultPanel != null)
            resultPanel.SetActive(true);
        if (tutorialHintText != null)
            tutorialHintText.gameObject.SetActive(false);

        bool tutorialMode = tutorialModeActive;
        bool ownedWon = ownedFighter != null && !ownedFighter.IsDefeated && (ownedFighter == player ? enemy.IsDefeated : player.IsDefeated);
        bool draw = remainingTime <= 0f && player != null && enemy != null && !player.IsDefeated && !enemy.IsDefeated;
        pendingReward = ownedWon ? tutorialMode ? TutorialVictoryReward : VictoryReward : 0;
        if (pendingReward > 0)
        {
            PlayerPrefs.SetInt(CryptaKey, PlayerPrefs.GetInt(CryptaKey, 0) + pendingReward);
            PlayerPrefs.Save();
        }

        if (resultTitleText != null)
        {
            if (tutorialMode)
                resultTitleText.text = "ОБУЧЕНИЕ ЗАВЕРШЕНО";
            else if (draw)
                resultTitleText.text = "НИЧЬЯ";
            else
                resultTitleText.text = ownedWon ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
        }

        if (resultDetailsText != null)
        {
            if (pendingReward > 0)
                resultDetailsText.text = "Награда: +" + pendingReward + " крипты. Нажми кнопку, чтобы вернуться в меню.";
            else
                resultDetailsText.text = tutorialMode ? "Теперь можно перейти в главное меню." : "Нажми кнопку, чтобы вернуться в меню.";
        }

        if (audioEvents != null)
            audioEvents.PlayFightEnd(ownedWon);
    }

    private void ReturnToMenu()
    {
        if (pvpNetwork != null)
            pvpNetwork.Shutdown();

        Time.timeScale = 1f;
        PlayerPrefs.DeleteKey(ArenaModeKey);
        PlayerPrefs.DeleteKey(SelectedCharacterKey);
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        if (tutorialModeActive)
        {
            PlayerPrefs.SetInt(TutorialCompletedKey, 1);
            PlayerPrefs.SetInt(TutorialVersionKey, CurrentTutorialVersion);
            PlayerPrefs.DeleteKey(TutorialModeKey);
        }
        PlayerPrefs.Save();
        SceneManager.LoadScene("Menu", LoadSceneMode.Single);
    }

    private void CreateResultPanel(Transform parent)
    {
        resultPanel = new GameObject("Fight Result Panel");
        RectTransform root = resultPanel.AddComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        Image overlay = resultPanel.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.72f);

        RectTransform window = new GameObject("Window").AddComponent<RectTransform>();
        window.SetParent(root, false);
        window.anchorMin = new Vector2(0.5f, 0.5f);
        window.anchorMax = window.anchorMin;
        window.pivot = new Vector2(0.5f, 0.5f);
        window.anchoredPosition = Vector2.zero;
        window.sizeDelta = new Vector2(620f, 320f);
        Image windowImage = window.gameObject.AddComponent<Image>();
        windowImage.color = new Color(0.06f, 0.06f, 0.06f, 0.96f);

        resultTitleText = CreateUiText(window, "ПОБЕДА", 48, TextAnchor.MiddleCenter, Color.red);
        RectTransform titleRect = resultTitleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -38f);
        titleRect.sizeDelta = new Vector2(0f, 78f);

        resultDetailsText = CreateUiText(window, "Нажми кнопку, чтобы вернуться в меню.", 24, TextAnchor.MiddleCenter, Color.white);
        RectTransform detailsRect = resultDetailsText.GetComponent<RectTransform>();
        detailsRect.anchorMin = new Vector2(0f, 0.5f);
        detailsRect.anchorMax = new Vector2(1f, 0.5f);
        detailsRect.pivot = new Vector2(0.5f, 0.5f);
        detailsRect.anchoredPosition = new Vector2(0f, 10f);
        detailsRect.sizeDelta = new Vector2(-60f, 54f);

        Button menuButton = CreateControlButton(window, "В МЕНЮ", new Vector2(0f, -96f), new Vector2(220f, 64f), 28);
        RectTransform buttonRect = menuButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = buttonRect.anchorMin;
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        menuButton.onClick.AddListener(ReturnToMenu);

        resultPanel.SetActive(false);
    }


    private void LoadRandomArenaScene()
    {
        if (ArenaSceneNames == null || ArenaSceneNames.Length == 0)
            return;

        loadedArenaSceneName = ArenaSceneNames[Random.Range(0, ArenaSceneNames.Length)];
        Scene arenaScene = SceneManager.GetSceneByName(loadedArenaSceneName);
        if (!arenaScene.isLoaded)
            SceneManager.LoadScene(loadedArenaSceneName, LoadSceneMode.Additive);

        StartCoroutine(ConfigureLoadedArenaCamera());
    }

    private IEnumerator ConfigureLoadedArenaCamera()
    {
        yield return null;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        Camera arenaCamera = null;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate != null && candidate.gameObject.scene.name == loadedArenaSceneName)
            {
                arenaCamera = candidate;
                break;
            }
        }

        AlignLoadedArenaToFightPlane();
        FixLoadedArenaMaterials();

        if (arenaCamera == null)
            yield break;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null || candidate == arenaCamera)
                continue;

            if (candidate.CompareTag("MainCamera"))
                candidate.tag = "Untagged";
        }

        arenaCamera.enabled = true;
        arenaCamera.tag = "MainCamera";
        arenaCamera.transform.position = ArenaCameraPosition;
        arenaCamera.transform.rotation = ArenaCameraRotation;
        arenaCamera.fieldOfView = ArenaCameraFov;
    }


    private void AlignLoadedArenaToFightPlane()
    {
        if (string.IsNullOrEmpty(loadedArenaSceneName))
            return;

        Scene arenaScene = SceneManager.GetSceneByName(loadedArenaSceneName);
        if (!arenaScene.isLoaded)
            return;

        Vector3 offset = new Vector3(0f, ArenaFightGroundY - LoadedArenaSourceGroundY, ArenaFightPlaneZ - LoadedArenaSourcePlaneZ);
        GameObject[] roots = arenaScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null || root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null)
                continue;

            root.transform.position += offset;
        }
    }
    private void FixLoadedArenaMaterials()
    {
        if (string.IsNullOrEmpty(loadedArenaSceneName))
            return;

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.gameObject.scene.name != loadedArenaSceneName)
                continue;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                if (!IsBrokenArenaMaterial(materials[materialIndex]))
                    continue;

                materials[materialIndex] = GetArenaFallbackMaterial(renderer.gameObject.name, materialIndex);
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static bool IsBrokenArenaMaterial(Material material)
    {
        if (material == null || material.shader == null)
            return true;

        string shaderName = material.shader.name;
        if (string.IsNullOrEmpty(shaderName) || shaderName.Contains("InternalErrorShader"))
            return true;

        Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        return color.r > 0.95f && color.g < 0.05f && color.b > 0.95f;
    }

    private static Material GetArenaFallbackMaterial(string objectName, int materialIndex)
    {
        Color color = GetArenaFallbackColor(objectName, materialIndex);
        string key = color.ToString();
        if (arenaMaterialCache.TryGetValue(key, out Material cachedMaterial))
            return cachedMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.name = "Arena Fallback " + key;
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        arenaMaterialCache[key] = material;
        return material;
    }

    private static Color GetArenaFallbackColor(string objectName, int materialIndex)
    {
        string name = objectName == null ? string.Empty : objectName.ToLowerInvariant();

        if (name.Contains("shadow") || name.Contains("navy") || name.Contains("deep sea"))
            return new Color(0.06f, 0.12f, 0.28f, 1f);
        if (name.Contains("mint") || name.Contains("fighting slab"))
            return new Color(0.44f, 0.96f, 0.72f, 1f);
        if (name.Contains("sand") || name.Contains("beach"))
            return new Color(1f, 0.78f, 0.42f, 1f);
        if (name.Contains("bubblegum") || name.Contains("pink") || name.Contains("coral"))
            return new Color(1f, 0.28f, 0.58f, 1f);
        if (name.Contains("purple") || name.Contains("lavender"))
            return new Color(0.62f, 0.38f, 1f, 1f);
        if (name.Contains("aqua") || name.Contains("cyan") || name.Contains("teal") || name.Contains("wave"))
            return new Color(0.05f, 0.84f, 1f, 1f);
        if (name.Contains("yellow") || name.Contains("sun") || name.Contains("cream") || name.Contains("trim"))
            return new Color(1f, 0.86f, 0.28f, 1f);
        if (name.Contains("palm") || name.Contains("leaf"))
            return new Color(0.12f, 0.72f, 0.36f, 1f);
        if (name.Contains("trunk") || name.Contains("plank") || name.Contains("boardwalk"))
            return new Color(0.72f, 0.42f, 0.22f, 1f);
        if (name.Contains("cloud") || name.Contains("foam"))
            return new Color(0.94f, 0.98f, 1f, 1f);
        if (name.Contains("crowd") || name.Contains("stall") || name.Contains("block") || name.Contains("tile"))
        {
            Color[] palette =
            {
                new Color(1f, 0.27f, 0.35f, 1f),
                new Color(0.12f, 0.78f, 1f, 1f),
                new Color(1f, 0.78f, 0.12f, 1f),
                new Color(0.46f, 0.92f, 0.34f, 1f),
                new Color(0.74f, 0.42f, 1f, 1f)
            };
            return palette[Mathf.Abs((name.GetHashCode() + materialIndex) % palette.Length)];
        }

        Color[] defaultPalette =
        {
            new Color(0.28f, 0.78f, 1f, 1f),
            new Color(1f, 0.42f, 0.62f, 1f),
            new Color(1f, 0.82f, 0.24f, 1f),
            new Color(0.44f, 0.92f, 0.66f, 1f)
        };
        return defaultPalette[Mathf.Abs((name.GetHashCode() + materialIndex) % defaultPalette.Length)];
    }
    private static void BuildArenaVisuals()
    {
        if (GameObject.Find("Brainrot Demo Arena Visuals") != null)
            return;

        GameObject root = new GameObject("Brainrot Demo Arena Visuals");

        Material floorMaterial = CreateArenaMaterial("Brainrot Arena Floor Material", new Color(0.95f, 0.95f, 0.88f, 1f));
        Material backMaterial = CreateArenaMaterial("Brainrot Arena Noise Material", new Color(0.95f, 0.98f, 1f, 1f));
        Material pinkMaterial = CreateArenaMaterial("Brainrot Arena Pink Material", new Color(1f, 0.28f, 0.62f, 1f));
        Material yellowMaterial = CreateArenaMaterial("Brainrot Arena Yellow Material", new Color(1f, 0.82f, 0.18f, 1f));
        Material cyanMaterial = CreateArenaMaterial("Brainrot Arena Cyan Material", new Color(0.08f, 0.82f, 1f, 1f));

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Brainrot Cartoon Floor";
        floor.transform.SetParent(root.transform, false);
        floor.transform.position = new Vector3(0f, -0.63f, 0.22f);
        floor.transform.localScale = new Vector3(15f, 0.08f, 4.6f);
        SetRendererMaterial(floor, floorMaterial);

        GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backdrop.name = "Brainrot Animated Noise Backdrop";
        backdrop.transform.SetParent(root.transform, false);
        backdrop.transform.position = new Vector3(0f, 2.1f, 2.25f);
        backdrop.transform.localScale = new Vector3(15f, 5.4f, 0.08f);
        SetRendererMaterial(backdrop, backMaterial);
        backdrop.AddComponent<ArenaNoiseAnimator>();

        CreateGlitchBlock(root.transform, "Glitch Billboard Left", new Vector3(-5.1f, 0.55f, 1.7f), new Vector3(0.65f, 1.55f, 0.14f), pinkMaterial);
        CreateGlitchBlock(root.transform, "Glitch Billboard Right", new Vector3(5.15f, 0.72f, 1.7f), new Vector3(0.65f, 1.95f, 0.14f), cyanMaterial);
        CreateGlitchBlock(root.transform, "Glitch Stage Sign", new Vector3(0f, 3.55f, 1.65f), new Vector3(2.4f, 0.35f, 0.12f), yellowMaterial);

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.transform.position = new Vector3(0f, 1.15f, -6.2f);
            mainCamera.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.68f, 0.9f, 1f, 1f);
        }

        EnsureArenaLight("Brainrot Arena Key Light", new Vector3(-2.5f, 5.5f, -4f), Quaternion.Euler(52f, 28f, 0f), 2.4f);
        EnsureArenaLight("Brainrot Arena Fill Light", new Vector3(3f, 3f, -3f), Quaternion.Euler(36f, -38f, 0f), 1.2f);
    }

    private static Material CreateArenaMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader);
        material.name = name;
        material.color = color;
        return material;
    }

    private static void CreateGlitchBlock(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.position = position;
        block.transform.localScale = scale;
        SetRendererMaterial(block, material);
        block.AddComponent<ArenaGlitchObject>();
    }

    private static void SetRendererMaterial(GameObject target, Material material)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material = material;
    }

    private static void EnsureArenaLight(string objectName, Vector3 position, Quaternion rotation, float intensity)
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

    private static bool UsesTouchControls()
    {
        return Application.isMobilePlatform || PlayerPrefs.GetString(InputModeKey, InputModeKeyboard) != InputModeKeyboard;
    }

    private static Text CreateTutorialHint(Transform parent)
    {
        RectTransform root = new GameObject("Tutorial Hint").AddComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = new Vector2(0.5f, 0f);
        root.anchorMax = root.anchorMin;
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 22f);
        root.sizeDelta = new Vector2(980f, 82f);

        Image back = root.gameObject.AddComponent<Image>();
        back.color = new Color(0f, 0f, 0f, 0.52f);

        string hint = UsesTouchControls()
            ? "ОБУЧЕНИЕ: джойстик - движение, JUMP - прыжок, 1/2/3 - удары. Победи Shoto."
            : "ОБУЧЕНИЕ: A/D - движение, W - прыжок, 1/2/3 - удары. Победи Shoto.";

        Text text = CreateUiText(root, hint, 24, TextAnchor.MiddleCenter, Color.white);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(18f, 8f);
        textRect.offsetMax = new Vector2(-18f, -8f);
        return text;
    }

    private void UpdateTutorialProgress()
    {
        if (!tutorialModeActive || tutorialHintText == null || player == null)
            return;

        if (!tutorialMoved && Mathf.Abs(player.transform.position.x - tutorialStartX) > 0.35f)
            tutorialMoved = true;

        if (!tutorialJumped && !player.NetworkGrounded)
            tutorialJumped = true;

        if (!tutorialAttacked && player.LastAttackSequence > 0)
            tutorialAttacked = true;

        if (!tutorialSuperSeenReady && player.SuperChargeNormalized >= 1f)
            tutorialSuperSeenReady = true;

        bool joystick = UsesTouchControls();
        if (!tutorialMoved)
            tutorialHintText.text = joystick ? "ОБУЧЕНИЕ: подвигай джойстик влево или вправо." : "ОБУЧЕНИЕ: нажми A или D, чтобы подвигаться.";
        else if (!tutorialJumped)
            tutorialHintText.text = joystick ? "ОБУЧЕНИЕ: нажми JUMP, чтобы прыгнуть." : "ОБУЧЕНИЕ: нажми W, чтобы прыгнуть.";
        else if (!tutorialAttacked)
            tutorialHintText.text = "ОБУЧЕНИЕ: нажми 1 или 2, чтобы ударить Shoto.";
        else if (!tutorialSuperSeenReady)
            tutorialHintText.text = "ОБУЧЕНИЕ: попадай по Shoto и получай урон, чтобы заполнить SUPER.";
        else if (player.SuperChargeNormalized >= 1f)
            tutorialHintText.text = "ОБУЧЕНИЕ: SUPER готов. Нажми 3.";
        else
            tutorialHintText.text = "ОБУЧЕНИЕ: отлично. Победи Shoto.";
    }

    private void CreateJoystickControls(Transform parent)
    {
        if (ownedFighter == null)
            return;

        RectTransform joystickRoot = CreateControlRoot(parent, "Move Joystick", Vector2.zero, Vector2.zero, Vector2.zero);
        joystickRoot.anchoredPosition = new Vector2(165f, 145f);
        joystickRoot.sizeDelta = new Vector2(260f, 260f);

        Image baseImage = joystickRoot.gameObject.AddComponent<Image>();
        baseImage.color = new Color(0f, 0f, 0f, 0.32f);

        RectTransform handle = CreateControlRoot(joystickRoot, "Handle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        handle.anchoredPosition = Vector2.zero;
        handle.sizeDelta = new Vector2(96f, 96f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(1f, 1f, 1f, 0.78f);

        MobileJoystick joystick = joystickRoot.gameObject.AddComponent<MobileJoystick>();
        joystick.Initialize(ownedFighter, joystickRoot, handle, 92f);

        Button jumpButton = CreateControlButton(parent, "JUMP", new Vector2(-395f, 128f), new Vector2(128f, 128f), 26);
        jumpButton.onClick.AddListener(ownedFighter.PressVirtualJump);

        Button attackOne = CreateControlButton(parent, "1", new Vector2(-250f, 92f), new Vector2(112f, 112f), 40);
        attackOne.onClick.AddListener(() => ownedFighter.PressVirtualAttack(1));

        Button attackTwo = CreateControlButton(parent, "2", new Vector2(-135f, 178f), new Vector2(112f, 112f), 40);
        attackTwo.onClick.AddListener(() => ownedFighter.PressVirtualAttack(2));

        Button attackThree = CreateControlButton(parent, "3", new Vector2(-78f, 54f), new Vector2(112f, 112f), 40);
        attackThree.onClick.AddListener(() => ownedFighter.PressVirtualAttack(3));
        superAttackButton = attackThree;
        superAttackButtonText = attackThree.GetComponentInChildren<Text>();
        UpdateSuperAttackButton();
    }

    private void UpdateSuperAttackButton()
    {
        if (superAttackButton == null || ownedFighter == null)
            return;

        bool ready = ownedFighter.SuperChargeNormalized >= 1f && !fightEnded;
        superAttackButton.interactable = ready;
        if (superAttackButtonText != null)
        {
            superAttackButtonText.text = ready ? "SUPER" : "3";
            superAttackButtonText.fontSize = ready ? 24 : 40;
        }
    }

    private static RectTransform CreateControlRoot(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        RectTransform rect = new GameObject(name).AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        return rect;
    }

    private static Button CreateControlButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size, int fontSize)
    {
        RectTransform rect = CreateControlRoot(parent, label + " Button", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.02f, 0.02f, 0.02f, 0.58f);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.02f, 0.02f, 0.02f, 0.58f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.72f);
        colors.pressedColor = new Color(0.85f, 0.05f, 0.05f, 0.78f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        Text text = CreateUiText(rect, label, fontSize, TextAnchor.MiddleCenter, Color.red);
        text.fontStyle = FontStyle.Bold;
        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, -2f);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private static void EnsureEventSystem()
    {
        BrainrotUiInput.EnsureEventSystem();
    }

    private static HealthBarView CreateCornerHealthBar(Transform parent, string label, Vector2 anchoredPosition, bool leftSide, Color fillColor)
    {
        var root = new GameObject(label + " Health").AddComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = leftSide ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        root.anchorMax = root.anchorMin;
        root.pivot = leftSide ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        root.anchoredPosition = anchoredPosition;
        root.sizeDelta = new Vector2(640f, 58f);

        Text nameText = CreateUiText(root, label, 20, leftSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, Color.white);
        RectTransform nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.anchoredPosition = new Vector2(0f, 0f);
        nameRect.sizeDelta = new Vector2(0f, 22f);

        var barBack = new GameObject("Back").AddComponent<RectTransform>();
        barBack.SetParent(root, false);
        barBack.anchorMin = new Vector2(0f, 0f);
        barBack.anchorMax = new Vector2(1f, 0f);
        barBack.pivot = new Vector2(0.5f, 0f);
        barBack.anchoredPosition = Vector2.zero;
        barBack.sizeDelta = new Vector2(0f, 32f);

        Image back = barBack.gameObject.AddComponent<Image>();
        back.color = new Color(0.03f, 0.03f, 0.03f, 0.9f);

        var fillRect = new GameObject("Fill").AddComponent<RectTransform>();
        fillRect.SetParent(barBack, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(4f, 4f);
        fillRect.offsetMax = new Vector2(-4f, -4f);

        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.color = fillColor;

        Text valueText = CreateUiText(barBack, "100 / 100", 18, TextAnchor.MiddleCenter, Color.white);
        RectTransform valueRect = valueText.GetComponent<RectTransform>();
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;

        return new HealthBarView
        {
            fillRect = fillRect,
            valueText = valueText,
            drainsFromRight = !leftSide
        };
    }

    private static SuperMeterView CreateSuperMeter(Transform parent, Vector2 anchoredPosition, bool leftSide)
    {
        var root = new GameObject((leftSide ? "Left" : "Right") + " Super Meter").AddComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = leftSide ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        root.anchorMax = root.anchorMin;
        root.pivot = leftSide ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        root.anchoredPosition = anchoredPosition;
        root.sizeDelta = new Vector2(420f, 26f);

        Image back = root.gameObject.AddComponent<Image>();
        back.color = new Color(0.02f, 0.02f, 0.02f, 0.88f);

        RectTransform fillRect = new GameObject("Fill").AddComponent<RectTransform>();
        fillRect.SetParent(root, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.zero;
        fillRect.offsetMin = new Vector2(4f, 4f);
        fillRect.offsetMax = new Vector2(-4f, -4f);

        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.color = new Color(0.15f, 0.58f, 1f, 1f);

        Text valueText = CreateUiText(root, "SUPER 0%", 15, TextAnchor.MiddleCenter, Color.white);
        RectTransform valueRect = valueText.GetComponent<RectTransform>();
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;

        return new SuperMeterView
        {
            fillRect = fillRect,
            valueText = valueText,
            fillsFromRight = !leftSide
        };
    }

    private static Text CreateOwnedMarker(Transform parent, out RectTransform root)
    {
        root = new GameObject("Owned Fighter Marker").AddComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = root.anchorMin;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(48f, 42f);

        Text text = CreateUiText(root, "✓", 34, TextAnchor.MiddleCenter, new Color(0.1f, 1f, 0.25f, 1f));
        text.fontStyle = FontStyle.Bold;
        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, -2f);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return text;
    }

    private static Text CreateTimerText(Transform parent)
    {
        var timerRect = new GameObject("Round Timer").AddComponent<RectTransform>();
        timerRect.SetParent(parent, false);
        timerRect.anchorMin = new Vector2(0.5f, 1f);
        timerRect.anchorMax = timerRect.anchorMin;
        timerRect.pivot = new Vector2(0.5f, 1f);
        timerRect.anchoredPosition = new Vector2(0f, -28f);
        timerRect.sizeDelta = new Vector2(160f, 70f);

        Text text = CreateUiText(timerRect, Mathf.CeilToInt(RoundDuration).ToString(), 52, TextAnchor.MiddleCenter, Color.white);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return text;
    }

    private static Text CreateUiText(Transform parent, string value, int fontSize, TextAnchor alignment, Color color)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);

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
}

public class MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private SahurFighterController fighter;
    private RectTransform root;
    private RectTransform handle;
    private float radius = 92f;

    public void Initialize(SahurFighterController targetFighter, RectTransform rootRect, RectTransform handleRect, float movementRadius)
    {
        fighter = targetFighter;
        root = rootRect;
        handle = handleRect;
        radius = movementRadius;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        UpdateStick(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        UpdateStick(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (handle != null)
            handle.anchoredPosition = Vector2.zero;

        if (fighter != null)
            fighter.SetVirtualMovement(0f);
    }

    private void UpdateStick(PointerEventData eventData)
    {
        if (root == null || handle == null || fighter == null)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
        Vector2 clamped = Vector2.ClampMagnitude(localPoint, radius);
        handle.anchoredPosition = clamped;

        float horizontal = Mathf.Abs(clamped.x) < radius * 0.18f ? 0f : clamped.x / radius;
        fighter.SetVirtualMovement(horizontal);
    }
}

public class ArenaNoiseAnimator : MonoBehaviour
{
    private Renderer cachedRenderer;
    private Color baseColor;

    private void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        if (cachedRenderer != null)
            baseColor = cachedRenderer.material.color;
    }

    private void Update()
    {
        if (cachedRenderer == null)
            return;

        float flicker = Random.Range(-0.045f, 0.045f);
        float wave = Mathf.Sin(Time.time * 18f) * 0.035f;
        cachedRenderer.material.color = new Color(
            Mathf.Clamp01(baseColor.r + flicker + wave),
            Mathf.Clamp01(baseColor.g + flicker),
            Mathf.Clamp01(baseColor.b + flicker - wave),
            1f);
    }
}

public class ArenaGlitchObject : MonoBehaviour
{
    private Renderer cachedRenderer;
    private Vector3 basePosition;
    private Vector3 baseScale;
    private Color baseColor;
    private float nextGlitchTime;

    private void Awake()
    {
        basePosition = transform.position;
        baseScale = transform.localScale;
        cachedRenderer = GetComponent<Renderer>();
        if (cachedRenderer != null)
            baseColor = cachedRenderer.material.color;
    }

    private void Update()
    {
        if (Time.time < nextGlitchTime)
            return;

        nextGlitchTime = Time.time + Random.Range(0.08f, 0.28f);
        transform.position = basePosition + new Vector3(Random.Range(-0.035f, 0.035f), Random.Range(-0.025f, 0.025f), 0f);
        transform.localScale = new Vector3(baseScale.x * Random.Range(0.96f, 1.08f), baseScale.y, baseScale.z);

        if (cachedRenderer != null)
        {
            float channelShift = Random.Range(-0.18f, 0.18f);
            cachedRenderer.material.color = new Color(
                Mathf.Clamp01(baseColor.r + channelShift),
                Mathf.Clamp01(baseColor.g - channelShift),
                Mathf.Clamp01(baseColor.b + Random.Range(-0.1f, 0.1f)),
                1f);
        }
    }
}

public class BrainrotAudioEvents : MonoBehaviour
{
    private const float FightMusicVolume = 0.5f;
    [SerializeField] private AudioClip fightStartClip;
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip superHitClip;
    [SerializeField] private AudioClip victoryClip;
    [SerializeField] private AudioClip defeatClip;

    private static BrainrotAudioEvents instance;
    private AudioSource source;

    public static BrainrotAudioEvents Ensure()
    {
        if (instance != null)
            return instance;

        GameObject audioObject = new GameObject("Brainrot Audio Events");
        instance = audioObject.AddComponent<BrainrotAudioEvents>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        source = GetComponent<AudioSource>();
        if (source == null)
            source = gameObject.AddComponent<AudioSource>();

        LoadDefaultClips();
    }


    private void LoadDefaultClips()
    {
        if (fightStartClip == null)
            fightStartClip = Resources.Load<AudioClip>("Audio/UI_Sounds/Fight_Theme_1");
        if (hitClip == null)
            hitClip = Resources.Load<AudioClip>("Audio/Punch1");
        if (superHitClip == null)
            superHitClip = Resources.Load<AudioClip>("Audio/Thud1");
        if (victoryClip == null)
            victoryClip = Resources.Load<AudioClip>("Audio/UI_Sounds/Button_Click_2");
        if (defeatClip == null)
            defeatClip = Resources.Load<AudioClip>("Audio/Break");
    }
    public void PlayFightStart()
    {
        Play(fightStartClip, FightMusicVolume);
    }

    public void PlayHit(bool superHit)
    {
        Play(superHit ? superHitClip : hitClip);
    }

    public void PlayFightEnd(bool victory)
    {
        Play(victory ? victoryClip : defeatClip);
    }

    private void Play(AudioClip clip, float volumeScale = 1f)
    {
        if (source != null && clip != null)
            source.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }
}



