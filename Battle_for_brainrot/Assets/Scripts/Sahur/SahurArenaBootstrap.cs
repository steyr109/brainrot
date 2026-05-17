using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SahurArenaBootstrap : MonoBehaviour
{
    private const string ArenaModeKey = "BattleForBrainrot.Mode";
    private const string ArenaModeValue = "Sahur";
    private const string SelectedCharacterKey = "SelectedCharacter";
    private const string RemoteSelectedCharacterKey = "RemoteSelectedCharacter";
    private const string SahurCharacterValue = "Sahur";
    private const string BalerinaCharacterValue = "BalerinaCapuchino";
    private const string GameModeKey = "BattleForBrainrot.GameMode";
    private const string GameModePvp = "PVP";
    private const float FighterScale = 0.27f;
    private const float SpawnY = -0.55f;
    private const float RoundDuration = 60f;

    private HealthBarView playerHealthBar;
    private HealthBarView enemyHealthBar;
    private Text timerText;
    private RectTransform ownedMarkerRoot;
    private SahurFighterController ownedFighter;
    private SahurPvpNetworkController pvpNetwork;
    private SahurFighterController player;
    private SahurFighterController enemy;
    private FighterDefinition leftFighterDefinition;
    private FighterDefinition rightFighterDefinition;
    private float remainingTime = RoundDuration;
    private bool fightEnded;

    private class HealthBarView
    {
        public RectTransform fillRect;
        public Text valueText;
        public bool drainsFromRight;
    }

    private struct FighterDefinition
    {
        public string displayName;
        public string resourceName;
        public float scale;
        public float spawnY;

        public FighterDefinition(string displayName, string resourceName, float scale, float spawnY)
        {
            this.displayName = displayName;
            this.resourceName = resourceName;
            this.scale = scale;
            this.spawnY = spawnY;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        bool hasSelectedCharacter = !string.IsNullOrEmpty(PlayerPrefs.GetString(SelectedCharacterKey, string.Empty));
        if (scene.name != "3DStage" || (PlayerPrefs.GetString(ArenaModeKey) != ArenaModeValue && !hasSelectedCharacter))
            return;

        var existing = FindObjectOfType<SahurArenaBootstrap>();
        if (existing == null)
            new GameObject("Brainrot Arena Bootstrap").AddComponent<SahurArenaBootstrap>();
    }

    private void Start()
    {
        ClearLegacyFightScene();
        SpawnFighters();
        BuildFightUi();
        UpdateHealthUi();
        UpdateOwnedMarker();
    }

    private void Update()
    {
        if (fightEnded)
            return;

        UpdateHealthUi();
        UpdateOwnedMarker();
        UpdateTimer();
        CheckFightEnd();
    }

    private static FighterDefinition GetSelectedFighter()
    {
        return GetFighterDefinition(PlayerPrefs.GetString(SelectedCharacterKey, SahurCharacterValue));
    }

    private static FighterDefinition GetRemoteFighter()
    {
        return GetFighterDefinition(PlayerPrefs.GetString(RemoteSelectedCharacterKey, SahurCharacterValue));
    }

    private static FighterDefinition GetFighterDefinition(string selectedCharacter)
    {
        if (selectedCharacter == BalerinaCharacterValue)
            return new FighterDefinition("BALERINA CAPUCHINO", BalerinaCharacterValue, FighterScale, SpawnY);

        return new FighterDefinition("SAHUR", SahurCharacterValue, FighterScale, SpawnY);
    }

    private void ClearLegacyFightScene()
    {
        var manager = FindObjectOfType<FightManager>();
        if (manager != null)
            manager.gameObject.SetActive(false);

        DisableSceneObject("GUI");
        DisableSceneObject("Rematch Canvas");
        DisableSceneObject("Round Animator");

        foreach (NewFighter legacyFighter in FindObjectsOfType<NewFighter>())
            Destroy(legacyFighter.gameObject);

        foreach (Projectile projectile in FindObjectsOfType<Projectile>())
            Destroy(projectile.gameObject);
    }

    private static void DisableSceneObject(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        if (target != null)
            target.SetActive(false);
    }

    private void SpawnFighters()
    {
        bool pvpMode = PlayerPrefs.GetString(GameModeKey, "PVE") == GameModePvp;
        bool hostOwnsLeft = Application.isEditor;

        FighterDefinition localDefinition = GetSelectedFighter();
        FighterDefinition remoteDefinition = pvpMode ? GetRemoteFighter() : localDefinition;
        leftFighterDefinition = !pvpMode || hostOwnsLeft ? localDefinition : remoteDefinition;
        rightFighterDefinition = !pvpMode || hostOwnsLeft ? remoteDefinition : localDefinition;

        GameObject leftPrefab = Resources.Load<GameObject>(leftFighterDefinition.resourceName);
        GameObject rightPrefab = Resources.Load<GameObject>(rightFighterDefinition.resourceName);
        if (leftPrefab == null || rightPrefab == null)
        {
            Debug.LogError("Selected fighter prefab was not found. Left: " + leftFighterDefinition.resourceName + ", Right: " + rightFighterDefinition.resourceName);
            return;
        }

        GameObject playerObject = Instantiate(leftPrefab, new Vector3(-2.6f, leftFighterDefinition.spawnY, 0f), Quaternion.identity);
        GameObject enemyObject = Instantiate(rightPrefab, new Vector3(2.6f, rightFighterDefinition.spawnY, 0f), Quaternion.identity);

        playerObject.name = leftFighterDefinition.displayName + " Player";
        enemyObject.name = rightFighterDefinition.displayName + " Opponent";
        playerObject.transform.localScale = Vector3.one * leftFighterDefinition.scale;
        enemyObject.transform.localScale = Vector3.one * rightFighterDefinition.scale;

        player = PrepareFighter(playerObject);
        enemy = PrepareFighter(enemyObject);

        bool playerIsLocal = !pvpMode || hostOwnsLeft;
        bool enemyIsLocal = pvpMode && !hostOwnsLeft;

        player.Initialize(playerIsLocal, enemy, 100, leftFighterDefinition.spawnY);
        enemy.Initialize(enemyIsLocal, player, 100, rightFighterDefinition.spawnY);
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

        playerHealthBar = CreateCornerHealthBar(canvas.transform, leftFighterDefinition.displayName, new Vector2(80f, -46f), true, new Color(0.12f, 0.82f, 0.35f, 1f));
        enemyHealthBar = CreateCornerHealthBar(canvas.transform, rightFighterDefinition.displayName, new Vector2(-80f, -46f), false, new Color(0.9f, 0.16f, 0.18f, 1f));
        timerText = CreateTimerText(canvas.transform);
        CreateOwnedMarker(canvas.transform, out ownedMarkerRoot);
    }

    private void UpdateHealthUi()
    {
        UpdateHealthBar(playerHealthBar, player);
        UpdateHealthBar(enemyHealthBar, enemy);
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
            StartCoroutine(EndFightAfterDelay());
    }

    private IEnumerator EndFightAfterDelay()
    {
        fightEnded = true;
        if (player != null)
            player.SetInputEnabled(false);
        if (enemy != null)
            enemy.SetInputEnabled(false);

        yield return new WaitForSeconds(2f);

        if (pvpNetwork != null)
            pvpNetwork.Shutdown();

        PlayerPrefs.DeleteKey(ArenaModeKey);
        PlayerPrefs.DeleteKey(SelectedCharacterKey);
        PlayerPrefs.DeleteKey(RemoteSelectedCharacterKey);
        SceneManager.LoadScene("Menu", LoadSceneMode.Single);
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


