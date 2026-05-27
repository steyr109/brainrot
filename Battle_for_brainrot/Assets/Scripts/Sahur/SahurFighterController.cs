using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class SahurFighterController : MonoBehaviour
{
    private static readonly int MoveSpeed = Animator.StringToHash("MoveSpeed");
    private static readonly int IsGrounded = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalVelocity = Animator.StringToHash("VerticalVelocity");
    private static readonly int LowPunch = Animator.StringToHash("LowPunch");
    private static readonly int HighPunch = Animator.StringToHash("HighPunch");
    private static readonly int SuperPunch = Animator.StringToHash("SuperPunch");
    private static readonly int Jump = Animator.StringToHash("Jump");
    private static readonly int JumpStart = Animator.StringToHash("Jump_Start");
    private static readonly int Idle = Animator.StringToHash("Idle");
    private static readonly int Hit = Animator.StringToHash("Hit");
    private static readonly int Knockdown = Animator.StringToHash("Knockdown");
    private static readonly int Block = Animator.StringToHash("Block");
    private static readonly int IsBlocking = Animator.StringToHash("IsBlocking");
    private const string InputModeKey = "BattleForBrainrot.InputMode";
    private const string InputModeKeyboard = "Keyboard";
    private const float MaxSuperChargeValue = 100f;
    private const float SuperGainOnHit = 24f;
    private const float SuperGainOnDamage = 18f;
    private const float SuperDamageMultiplier = 1.2f;
    private const float SuperKnockbackChance = 0.45f;
    private const float SuperKnockbackForce = 7.5f;
    private const float KnockbackDamping = 18f;
    private const float HitPauseDuration = 0.055f;
    private const float SuperHitPauseDuration = 0.085f;
    private const int TutorialAiBasicAttackDamage = 4;
    private const float TutorialAiBasicAttackRecovery = 1.05f;

    private const float LightImpactShakeStrength = 0.075f;
    private const float HeavyImpactShakeStrength = 0.12f;
    private const float SuperImpactShakeStrength = 0.18f;
    private const float ImpactShakeDuration = 0.13f;
    private const float SuperImpactShakeDuration = 0.18f;
    private const float StepDustCooldown = 0.16f;
    private const float ImpactFxZ = -0.85f;

    [SerializeField] private bool playerControlled = true;
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float gravity = 22f;
    [SerializeField] private float stageLimit = 7f;
    [SerializeField] private float hurtboxHalfWidth = 0.45f;
    [SerializeField] private float hurtboxHeight = 1.25f;
    [Header("Simple AI")]
    [SerializeField] private float aiPreferredDistance = 1.35f;
    [SerializeField] private float aiDecisionInterval = 0.35f;
    [SerializeField] private float aiAttackChance = 0.42f;
    [SerializeField] private float aiJumpChance = 0.08f;

    private const float RemoteLerpSpeed = 14f;

    private Animator animator;
    private SahurFighterController opponent;
    private SahurPvpNetworkController pvpNetwork;
    private Coroutine attackCoroutine;
    private float verticalVelocity;
    private float groundY;
    private float nextAttackTime;
    private float knockbackVelocity;
    private bool grounded = true;
    private bool blocking;
    private bool defeated;
    private bool inputEnabled = true;
    private bool aiEnabled = true;
    private bool remoteControlled;
    private Vector3 remoteTargetPosition;
    private Quaternion remoteTargetRotation;
    private float nextAiDecisionTime;
    private float aiMoveDirection;
    private int attackSequence;
    private int lastAttackTrigger;
    private int lastAppliedRemoteAttackSequence = -1;
    private int lastRemoteHitHealth = -1;
    private bool remoteKnockdownPlayed;
    private bool wasRemoteGrounded = true;
    private float virtualHorizontal;
    private bool virtualBlocking;
    private bool basicAttackOnlyAi;
    private bool shotoIdleStarted;
    private string shotoAnimationState;
    private static bool hitPauseActive;
    private static float hitPausePreviousTimeScale = 1f;
    private static float hitPausePreviousFixedDeltaTime = 0.02f;
    private static Material impactMaterial;
    private static Material superImpactMaterial;
    private static Material dustMaterial;

    private float nextStepDustTime;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public float CurrentSuperCharge { get; private set; }
    public float MaxSuperCharge => MaxSuperChargeValue;
    public float SuperChargeNormalized => Mathf.Clamp01(CurrentSuperCharge / MaxSuperChargeValue);
    public bool IsDefeated => defeated;
    public bool IsPlayerControlled => playerControlled;
    public float NetworkMoveSpeed { get; private set; }
    public bool NetworkGrounded => grounded;
    public float NetworkVerticalVelocity => verticalVelocity;
    public bool NetworkBlocking => blocking;
    public int LastAttackSequence => attackSequence;
    public int LastAttackTrigger => lastAttackTrigger;

    private struct AttackTiming
    {
        public int triggerHash;
        public int damage;
        public float startup;
        public float activeTime;
        public float recovery;
        public float range;
        public float height;
        public float verticalOffset;
        public bool isSuper;

        public AttackTiming(int triggerHash, int damage, float startup, float activeTime, float recovery, float range, float height, float verticalOffset, bool isSuper = false)
        {
            this.triggerHash = triggerHash;
            this.damage = damage;
            this.startup = startup;
            this.activeTime = activeTime;
            this.recovery = recovery;
            this.range = range;
            this.height = height;
            this.verticalOffset = verticalOffset;
            this.isSuper = isSuper;
        }
    }

    public void Initialize(bool controlledByPlayer, SahurFighterController enemy, int health, float groundHeight)
    {
        playerControlled = controlledByPlayer;
        opponent = enemy;
        maxHealth = health;
        CurrentHealth = maxHealth;
        CurrentSuperCharge = 0f;
        groundY = groundHeight;
        verticalVelocity = 0f;
        grounded = true;
        defeated = false;
        remoteControlled = false;
        remoteKnockdownPlayed = false;
        wasRemoteGrounded = true;
        inputEnabled = true;
        aiEnabled = !controlledByPlayer;
        NetworkMoveSpeed = 0f;
        knockbackVelocity = 0f;

        Vector3 position = transform.position;
        position.y = groundY;
        transform.position = position;
        remoteTargetPosition = position;
        remoteTargetRotation = transform.rotation;

        animator = GetComponentInChildren<Animator>();
        SetAnimatorGrounded();
        SetAnimatorFloat(VerticalVelocity, 0f);
    }

    public void SetOpponent(SahurFighterController enemy)
    {
        opponent = enemy;
    }

    public void SetPvpNetwork(SahurPvpNetworkController networkController)
    {
        pvpNetwork = networkController;
    }

    public void SetAiEnabled(bool enabled)
    {
        aiEnabled = enabled;
        if (!enabled)
            aiMoveDirection = 0f;
    }

    public void SetBasicAttackOnlyAi(bool enabled)
    {
        basicAttackOnlyAi = enabled;
        if (enabled)
            aiMoveDirection = 0f;
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
        {
            blocking = false;
            virtualHorizontal = 0f;
            virtualBlocking = false;
            NetworkMoveSpeed = 0f;
            SetAnimatorFloat(MoveSpeed, 0f);
            SetAnimatorBool(IsBlocking, false);
        }
    }

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        CurrentHealth = maxHealth;
        groundY = transform.position.y;
        remoteTargetPosition = transform.position;
        remoteTargetRotation = transform.rotation;
    }

    private void OnDisable()
    {
        ResetHitPauseIfNeeded();
    }

    private void Update()
    {
        if (remoteControlled)
        {
            float t = 1f - Mathf.Exp(-RemoteLerpSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, remoteTargetPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, remoteTargetRotation, t);
            return;
        }

        if (defeated)
            return;

        FaceOpponent();

        float horizontal = 0f;
        if (inputEnabled)
        {
            if (playerControlled)
            {
                horizontal = GetMovementInput();
                HandleJumpInput();
                HandleBlockInput();
                HandleAttackInput();
            }
            else
            {
                horizontal = UpdateAi();
            }
        }
        else
        {
            blocking = false;
        }

        Move(horizontal);
        ApplyKnockbackMotion();
        ApplyGravity();
        UpdateAnimator(horizontal);
    }

    private float GetMovementInput()
    {
        float input = UsesVirtualInput() ? virtualHorizontal : 0f;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed) input -= 1f;
            if (keyboard.dKey.isPressed) input += 1f;
        }

        return Mathf.Clamp(input, -1f, 1f);
    }

    private void HandleJumpInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && grounded && keyboard.wKey.wasPressedThisFrame)
        {
            BeginJump();
        }
    }

    private void HandleBlockInput()
    {
        if (UsesVirtualInput())
        {
            blocking = grounded && virtualBlocking;
            return;
        }

        Keyboard keyboard = Keyboard.current;
        blocking = keyboard != null && grounded && keyboard.sKey.isPressed;
        if (keyboard != null && blocking && keyboard.sKey.wasPressedThisFrame)
            SetAnimatorTrigger(Block);
    }

    private void HandleAttackInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || Time.time < nextAttackTime || blocking || attackCoroutine != null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
            TryStartAttack(1);
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
            TryStartAttack(2);
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
            TryStartAttack(3);
    }

    public void SetVirtualMovement(float horizontal)
    {
        virtualHorizontal = Mathf.Clamp(horizontal, -1f, 1f);
    }

    public void SetVirtualBlock(bool block)
    {
        virtualBlocking = block;
    }

    public void PressVirtualJump()
    {
        if (!inputEnabled || defeated || !playerControlled || !grounded)
            return;

        BeginJump();
    }

    public void PressVirtualAttack(int attackIndex)
    {
        if (!inputEnabled || defeated || !playerControlled)
            return;

        TryStartAttack(attackIndex);
    }

    private bool UsesVirtualInput()
    {
        return Application.isMobilePlatform || PlayerPrefs.GetString(InputModeKey, InputModeKeyboard) != InputModeKeyboard;
    }

    private void TryStartAttack(int attackIndex)
    {
        if (Time.time < nextAttackTime || blocking || attackCoroutine != null)
            return;

        if (attackIndex == 1)
            StartAttack(new AttackTiming(LowPunch, 8, 0.16f, 0.12f, 0.18f, 1.25f, 0.75f, 0.65f));
        else if (attackIndex == 2)
            StartAttack(new AttackTiming(HighPunch, 12, 0.24f, 0.14f, 0.22f, 1.55f, 0.85f, 0.95f));
        else if (attackIndex == 3 && CurrentSuperCharge >= MaxSuperChargeValue)
            StartAttack(new AttackTiming(SuperPunch, 22, 0.36f, 0.18f, 0.36f, 1.9f, 1.0f, 0.85f, true));
    }

    private float UpdateAi()
    {
        blocking = false;

        if (opponent == null || opponent.defeated)
            return 0f;

        if (Time.time >= nextAiDecisionTime)
        {
            nextAiDecisionTime = Time.time + aiDecisionInterval + Random.Range(0f, 0.25f);
            float distance = Mathf.Abs(opponent.transform.position.x - transform.position.x);
            float directionToOpponent = Mathf.Sign(opponent.transform.position.x - transform.position.x);

            if (basicAttackOnlyAi)
            {
                if (attackCoroutine == null && Time.time >= nextAttackTime && distance <= 1.65f)
                    StartAttack(new AttackTiming(LowPunch, TutorialAiBasicAttackDamage, 0.16f, 0.18f, TutorialAiBasicAttackRecovery, 1.45f, 0.9f, 0.65f));

                aiMoveDirection = distance > 1.35f ? directionToOpponent : 0f;
                return aiMoveDirection;
            }

            if (attackCoroutine == null && Time.time >= nextAttackTime && distance <= 1.9f && Random.value < aiAttackChance)
            {
                int attackIndex = CurrentSuperCharge >= MaxSuperChargeValue ? Random.Range(0, 3) : Random.Range(0, 2);
                if (attackIndex == 0)
                    StartAttack(new AttackTiming(LowPunch, 8, 0.16f, 0.12f, 0.18f, 1.25f, 0.75f, 0.65f));
                else if (attackIndex == 1)
                    StartAttack(new AttackTiming(HighPunch, 12, 0.24f, 0.14f, 0.22f, 1.55f, 0.85f, 0.95f));
                else
                    StartAttack(new AttackTiming(SuperPunch, 22, 0.36f, 0.18f, 0.36f, 1.9f, 1.0f, 0.85f, true));
            }

            if (grounded && Random.value < aiJumpChance)
            {
                BeginJump();
            }

            if (distance > aiPreferredDistance + 0.25f)
                aiMoveDirection = directionToOpponent;
            else if (distance < aiPreferredDistance - 0.25f)
                aiMoveDirection = -directionToOpponent;
            else
                aiMoveDirection = Random.value < 0.35f ? -directionToOpponent : 0f;
        }

        if (attackCoroutine != null)
            return 0f;

        return aiMoveDirection;
    }

    private void StartAttack(AttackTiming attack)
    {
        if (attack.isSuper)
            CurrentSuperCharge = 0f;

        nextAttackTime = Time.time + attack.startup + attack.activeTime + attack.recovery;
        attackSequence++;
        lastAttackTrigger = attack.triggerHash;
        if (basicAttackOnlyAi)
        {
            shotoIdleStarted = false;
            shotoAnimationState = null;
            PlayShotoAnimation("5A", 0.05f);
        }
        else
        {
            SetAnimatorTrigger(attack.triggerHash);
        }
        attackCoroutine = StartCoroutine(AttackRoutine(attack));
    }

    private IEnumerator AttackRoutine(AttackTiming attack)
    {
        yield return new WaitForSeconds(attack.startup);

        float activeUntil = Time.time + attack.activeTime;
        bool hasHit = false;

        while (Time.time < activeUntil)
        {
            if (!hasHit && IsOpponentInsideHitbox(attack))
            {
                int damage = attack.isSuper ? Mathf.CeilToInt(attack.damage * SuperDamageMultiplier) : attack.damage;
                bool shouldKnockback = attack.isSuper && Random.value <= SuperKnockbackChance;
                float knockbackDirection = Mathf.Sign(opponent.transform.position.x - transform.position.x);
                opponent.TakeDamage(damage);
                if (shouldKnockback)
                    opponent.ApplyKnockback(knockbackDirection, SuperKnockbackForce);
                GainSuper(SuperGainOnHit);
                BrainrotAudioEvents.Ensure().PlayHit(attack.isSuper);
                PlayImpactFeedback(attack);
                StartCoroutine(HitPauseRoutine(attack.isSuper ? SuperHitPauseDuration : HitPauseDuration));
                pvpNetwork?.SendDamageToRemote(damage, shouldKnockback, knockbackDirection);
                hasHit = true;
            }

            yield return null;
        }

        attackCoroutine = null;
    }

    private bool IsOpponentInsideHitbox(AttackTiming attack)
    {
        if (opponent == null || opponent.defeated)
            return false;

        float facing = GetFacingDirection();
        float opponentOffsetX = opponent.transform.position.x - transform.position.x;
        if (Mathf.Sign(opponentOffsetX) != facing)
            return false;

        float hitboxCenterX = transform.position.x + facing * (attack.range * 0.5f);
        float hitboxMinX = hitboxCenterX - attack.range * 0.5f;
        float hitboxMaxX = hitboxCenterX + attack.range * 0.5f;
        float hurtboxMinX = opponent.transform.position.x - opponent.hurtboxHalfWidth;
        float hurtboxMaxX = opponent.transform.position.x + opponent.hurtboxHalfWidth;

        float hitboxCenterY = transform.position.y + attack.verticalOffset;
        float hitboxMinY = hitboxCenterY - attack.height * 0.5f;
        float hitboxMaxY = hitboxCenterY + attack.height * 0.5f;
        float hurtboxMinY = opponent.transform.position.y;
        float hurtboxMaxY = opponent.transform.position.y + opponent.hurtboxHeight;

        bool overlapsX = hitboxMinX <= hurtboxMaxX && hitboxMaxX >= hurtboxMinX;
        bool overlapsY = hitboxMinY <= hurtboxMaxY && hitboxMaxY >= hurtboxMinY;
        return overlapsX && overlapsY;
    }

    public void TakeDamage(int amount)
    {
        if (defeated)
            return;

        int finalDamage = blocking ? Mathf.CeilToInt(amount * 0.35f) : amount;
        CurrentHealth = Mathf.Max(0, CurrentHealth - finalDamage);
        GainSuper(SuperGainOnDamage);

        if (CurrentHealth <= 0)
        {
            defeated = true;
            inputEnabled = false;
            PlayKnockdown();
            return;
        }

        if (basicAttackOnlyAi)
        {
            shotoIdleStarted = false;
            shotoAnimationState = null;
            PlayShotoAnimation("HitLight", 0.05f);
        }
        else
        {
            SetAnimatorTrigger(Hit);
        }
    }

    private void PlayImpactFeedback(AttackTiming attack)
    {
        if (opponent == null)
            return;

        float hitX = Mathf.Lerp(transform.position.x, opponent.transform.position.x, 0.5f);
        float hitY = Mathf.Max(transform.position.y, opponent.transform.position.y) + attack.verticalOffset;
        Vector3 impactPosition = new Vector3(hitX, hitY, ImpactFxZ);

        SpawnImpactEffect(impactPosition, attack.isSuper);
        float shakeStrength = attack.isSuper ? SuperImpactShakeStrength : attack.damage >= 12 ? HeavyImpactShakeStrength : LightImpactShakeStrength;
        float shakeDuration = attack.isSuper ? SuperImpactShakeDuration : ImpactShakeDuration;
        StartCoroutine(CameraShakeRoutine(shakeStrength, shakeDuration));
        StartCoroutine(CameraPunchRoutine(attack.isSuper ? 3.0f : 1.6f, shakeDuration));
    }

    private void SpawnImpactEffect(Vector3 position, bool superHit)
    {
        Material material = superHit ? GetSuperImpactMaterial() : GetImpactMaterial();
        float baseSize = superHit ? 1.25f : 0.85f;

        CreateFxCube("Sahur Impact Core", position, Vector3.one * (baseSize * 0.34f), Quaternion.Euler(0f, 0f, 45f), material, 0.18f, 1.45f);

        for (int i = 0; i < 5; i++)
        {
            float angle = -55f + i * 27.5f + Random.Range(-8f, 8f);
            Vector3 offset = new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(-0.12f, 0.12f), 0f);
            Vector3 scale = new Vector3(baseSize * Random.Range(0.55f, 0.95f), baseSize * Random.Range(0.055f, 0.09f), 0.08f);
            CreateFxCube("Sahur Impact Slash", position + offset, scale, Quaternion.Euler(0f, 0f, angle), material, 0.16f, 1.25f);
        }
    }

    private void SpawnDustEffect(Vector3 position, float size)
    {
        Material material = GetDustMaterial();
        int count = Mathf.Max(2, Mathf.RoundToInt(size * 3f));
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-0.22f, 0.22f) * size, Random.Range(-0.02f, 0.1f) * size, 0f);
            Vector3 scale = new Vector3(Random.Range(0.24f, 0.42f) * size, Random.Range(0.035f, 0.07f) * size, 0.06f);
            float angle = Random.Range(-18f, 18f);
            CreateFxCube("Sahur Movement Dust", position + offset, scale, Quaternion.Euler(0f, 0f, angle), material, 0.20f, 1.9f);
        }
    }

    private void CreateFxCube(string objectName, Vector3 position, Vector3 scale, Quaternion rotation, Material material, float lifetime, float endScaleMultiplier)
    {
        GameObject fx = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fx.name = objectName;
        fx.layer = 0;
        fx.transform.position = position;
        fx.transform.rotation = rotation;
        fx.transform.localScale = scale;

        Collider collider = fx.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        Renderer renderer = fx.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material = material;
        }

        StartCoroutine(AnimateFxCube(fx.transform, scale, lifetime, endScaleMultiplier));
    }

    private static IEnumerator AnimateFxCube(Transform target, Vector3 startScale, float lifetime, float endScaleMultiplier)
    {
        float elapsed = 0f;
        while (target != null && elapsed < lifetime)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);
            float scale = Mathf.Lerp(1f, endScaleMultiplier, t);
            target.localScale = startScale * scale;
            target.position += Vector3.up * (0.35f * Time.unscaledDeltaTime);
            yield return null;
        }

        if (target != null)
            Destroy(target.gameObject);
    }

    private static IEnumerator CameraShakeRoutine(float strength, float duration)
    {
        Camera camera = Camera.main;
        if (camera == null || strength <= 0f || duration <= 0f)
            yield break;

        Transform cameraTransform = camera.transform;
        Vector3 basePosition = cameraTransform.position;
        float elapsed = 0f;

        while (cameraTransform != null && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float fade = 1f - Mathf.Clamp01(elapsed / duration);
            Vector2 shake = Random.insideUnitCircle * (strength * fade);
            cameraTransform.position = basePosition + new Vector3(shake.x, shake.y, 0f);
            yield return null;
        }

        if (cameraTransform != null)
            cameraTransform.position = basePosition;
    }

    private static IEnumerator CameraPunchRoutine(float fovKick, float duration)
    {
        Camera camera = Camera.main;
        if (camera == null || fovKick <= 0f || duration <= 0f)
            yield break;

        float baseFov = camera.fieldOfView;
        float elapsed = 0f;

        while (camera != null && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curve = 1f - Mathf.Abs((t * 2f) - 1f);
            camera.fieldOfView = baseFov - fovKick * curve;
            yield return null;
        }

        if (camera != null)
            camera.fieldOfView = baseFov;
    }

    private static Material GetImpactMaterial()
    {
        if (impactMaterial == null)
            impactMaterial = CreateFxMaterial("Sahur Impact Material", new Color(1f, 0.72f, 0.06f, 1f));
        return impactMaterial;
    }

    private static Material GetSuperImpactMaterial()
    {
        if (superImpactMaterial == null)
            superImpactMaterial = CreateFxMaterial("Sahur Super Impact Material", new Color(1f, 0.18f, 0.04f, 1f));
        return superImpactMaterial;
    }

    private static Material GetDustMaterial()
    {
        if (dustMaterial == null)
            dustMaterial = CreateFxMaterial("Sahur Dust Material", new Color(0.82f, 0.72f, 0.52f, 1f));
        return dustMaterial;
    }

    private static Material CreateFxMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        return material;
    }

    private static IEnumerator HitPauseRoutine(float duration)
    {
        if (hitPauseActive || duration <= 0f)
            yield break;

        hitPauseActive = true;
        hitPausePreviousTimeScale = Time.timeScale;
        hitPausePreviousFixedDeltaTime = Time.fixedDeltaTime;
        Time.timeScale = 0.08f;
        Time.fixedDeltaTime = hitPausePreviousFixedDeltaTime * Time.timeScale;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = hitPausePreviousTimeScale;
        Time.fixedDeltaTime = hitPausePreviousFixedDeltaTime;
        hitPauseActive = false;
    }

    private static void ResetHitPauseIfNeeded()
    {
        if (!hitPauseActive)
            return;

        Time.timeScale = hitPausePreviousTimeScale;
        Time.fixedDeltaTime = hitPausePreviousFixedDeltaTime;
        hitPauseActive = false;
    }

    private void PlayKnockdown()
    {
        if (animator == null)
            return;

        if (basicAttackOnlyAi)
        {
            shotoIdleStarted = false;
            shotoAnimationState = null;
            PlayShotoAnimation("Knockdown", 0.05f);
            return;
        }

        ResetAnimatorTrigger(Hit);
        ResetAnimatorTrigger(Jump);
        SetAnimatorBool(IsBlocking, false);
        SetAnimatorTrigger(Knockdown);
        if (!CrossFadeAnimatorState(Knockdown, 0.05f))
            CrossFadeAnimatorState(Idle, 0.05f);
    }
    public void ApplyRemoteState(
        Vector3 position,
        Quaternion rotation,
        int health,
        bool isDefeated,
        float moveSpeed,
        bool isGrounded,
        float remoteVerticalVelocity,
        bool remoteBlocking,
        int remoteAttackSequence,
        int remoteAttackTrigger)
    {
        remoteControlled = true;
        inputEnabled = false;
        aiEnabled = false;
        blocking = remoteBlocking;
        bool shouldTriggerRemoteJump = wasRemoteGrounded && !isGrounded;
        bool shouldTriggerRemoteLanding = !wasRemoteGrounded && isGrounded;
        wasRemoteGrounded = isGrounded;
        grounded = isGrounded;
        verticalVelocity = remoteVerticalVelocity;
        NetworkMoveSpeed = moveSpeed;
        remoteTargetPosition = position;
        remoteTargetRotation = rotation;

        int incomingHealth = Mathf.Clamp(health, 0, maxHealth);
        if (incomingHealth < CurrentHealth)
        {
            CurrentHealth = incomingHealth;
            if (CurrentHealth > 0 && lastRemoteHitHealth != CurrentHealth)
            {
                SetAnimatorTrigger(Hit);
                lastRemoteHitHealth = CurrentHealth;
            }
        }

        defeated = isDefeated || CurrentHealth <= 0;

        if (animator != null)
        {
            SetAnimatorFloat(MoveSpeed, moveSpeed);
            SetAnimatorBool(IsGrounded, isGrounded);
            SetAnimatorFloat(VerticalVelocity, remoteVerticalVelocity);
            SetAnimatorBool(IsBlocking, remoteBlocking);

            if (shouldTriggerRemoteJump)
            {
                ResetAnimatorTrigger(Jump);
                SetAnimatorTrigger(Jump);
                CrossFadeAnimatorState(JumpStart, 0.03f);
            }

            if (shouldTriggerRemoteLanding)
            {
                ResetAnimatorTrigger(Jump);
                CrossFadeAnimatorState(Idle, 0.05f);
            }

            if (remoteAttackSequence != lastAppliedRemoteAttackSequence && remoteAttackTrigger != 0)
            {
                SetAnimatorTrigger(remoteAttackTrigger);
                lastAppliedRemoteAttackSequence = remoteAttackSequence;
            }

            if (defeated && !remoteKnockdownPlayed)
            {
                PlayKnockdown();
                remoteKnockdownPlayed = true;
            }
        }
    }

    public void ApplyNetworkDamage(int amount)
    {
        TakeDamage(amount);
    }

    public void ApplyNetworkDamage(int amount, bool hasKnockback, float knockbackDirection)
    {
        TakeDamage(amount);
        if (hasKnockback)
            ApplyKnockback(knockbackDirection, SuperKnockbackForce);
    }

    public void ApplyKnockback(float direction, float force)
    {
        if (defeated || Mathf.Approximately(direction, 0f) || force <= 0f)
            return;

        knockbackVelocity = Mathf.Sign(direction) * force;
        blocking = false;
    }

    public void ApplyRemoteSuperCharge(float value)
    {
        CurrentSuperCharge = Mathf.Clamp(value, 0f, MaxSuperChargeValue);
    }

    private void GainSuper(float amount)
    {
        if (defeated || amount <= 0f)
            return;

        CurrentSuperCharge = Mathf.Clamp(CurrentSuperCharge + amount, 0f, MaxSuperChargeValue);
    }

    private void Move(float horizontal)
    {
        Vector3 position = transform.position;
        float previousX = position.x;
        position.x = Mathf.Clamp(position.x + horizontal * walkSpeed * Time.deltaTime, -stageLimit, stageLimit);
        transform.position = position;

        if (grounded && Mathf.Abs(position.x - previousX) > 0.0025f && Time.unscaledTime >= nextStepDustTime)
        {
            nextStepDustTime = Time.unscaledTime + StepDustCooldown;
            Vector3 dustPosition = transform.position + new Vector3(-Mathf.Sign(horizontal) * 0.18f, 0.08f, ImpactFxZ);
            SpawnDustEffect(dustPosition, 0.55f);
        }
    }

    private void ApplyKnockbackMotion()
    {
        if (Mathf.Abs(knockbackVelocity) <= 0.01f)
        {
            knockbackVelocity = 0f;
            return;
        }

        Vector3 position = transform.position;
        position.x = Mathf.Clamp(position.x + knockbackVelocity * Time.deltaTime, -stageLimit, stageLimit);
        transform.position = position;
        knockbackVelocity = Mathf.MoveTowards(knockbackVelocity, 0f, KnockbackDamping * Time.deltaTime);
    }

    private void BeginJump()
    {
        verticalVelocity = jumpForce;
        grounded = false;
        SpawnDustEffect(transform.position + new Vector3(0f, 0.08f, ImpactFxZ), 0.8f);

        if (animator == null)
            return;

        ResetAnimatorTrigger(Jump);
        SetAnimatorBool(IsGrounded, false);
        SetAnimatorFloat(VerticalVelocity, verticalVelocity);
        SetAnimatorTrigger(Jump);
        CrossFadeAnimatorState(JumpStart, 0.03f);
    }

    private void Land()
    {
        verticalVelocity = 0f;
        grounded = true;
        SpawnDustEffect(transform.position + new Vector3(0f, 0.08f, ImpactFxZ), 1.0f);

        if (animator == null)
            return;

        ResetAnimatorTrigger(Jump);
        SetAnimatorBool(IsGrounded, true);
        SetAnimatorFloat(VerticalVelocity, 0f);
        CrossFadeAnimatorState(Idle, 0.05f);
    }
    private void ApplyGravity()
    {
        if (!grounded)
        {
            verticalVelocity -= gravity * Time.deltaTime;
            transform.position += Vector3.up * verticalVelocity * Time.deltaTime;

            if (transform.position.y <= groundY)
            {
                Vector3 position = transform.position;
                position.y = groundY;
                transform.position = position;
                Land();
            }
        }
    }

    private void FaceOpponent()
    {
        if (opponent == null)
            return;

        float direction = Mathf.Sign(opponent.transform.position.x - transform.position.x);
        if (Mathf.Approximately(direction, 0f))
            return;

        transform.rotation = Quaternion.LookRotation(Vector3.left * direction, Vector3.up);
    }

    private float GetFacingDirection()
    {
        return -Mathf.Sign(transform.forward.x);
    }

    private void UpdateAnimator(float horizontal)
    {
        NetworkMoveSpeed = Mathf.Abs(horizontal);

        if (animator == null)
            return;

        if (basicAttackOnlyAi)
        {
            if (attackCoroutine == null && !defeated && !shotoIdleStarted)
            {
                PlayShotoAnimation(Mathf.Abs(horizontal) > 0.05f ? "WalkForward" : "Idle", 0.08f);
                shotoIdleStarted = true;
            }
            else if (attackCoroutine == null && !defeated)
            {
                string locomotionState = Mathf.Abs(horizontal) > 0.05f ? "WalkForward" : "Idle";
                if (shotoAnimationState != locomotionState)
                    PlayShotoAnimation(locomotionState, 0.08f);
            }
            return;
        }

        SetAnimatorFloat(MoveSpeed, NetworkMoveSpeed);
        SetAnimatorBool(IsBlocking, blocking);
        SetAnimatorFloat(VerticalVelocity, verticalVelocity);
        SetAnimatorGrounded();
    }

    private void SetAnimatorGrounded()
    {
        if (animator != null && !basicAttackOnlyAi)
            SetAnimatorBool(IsGrounded, grounded);
    }

    private void PlayShotoAnimation(string stateName, float transition)
    {
        if (animator == null || shotoAnimationState == stateName)
            return;

        shotoAnimationState = stateName;
        CrossFadeAnimatorState(stateName, transition);
    }

    private bool HasAnimatorParameter(int parameterHash)
    {
        if (animator == null)
            return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == parameterHash)
                return true;
        }

        return false;
    }

    private void SetAnimatorFloat(int parameterHash, float value)
    {
        if (HasAnimatorParameter(parameterHash))
            animator.SetFloat(parameterHash, value);
    }

    private void SetAnimatorBool(int parameterHash, bool value)
    {
        if (HasAnimatorParameter(parameterHash))
            animator.SetBool(parameterHash, value);
    }

    private void SetAnimatorTrigger(int parameterHash)
    {
        if (HasAnimatorParameter(parameterHash))
            animator.SetTrigger(parameterHash);
    }

    private void ResetAnimatorTrigger(int parameterHash)
    {
        if (HasAnimatorParameter(parameterHash))
            animator.ResetTrigger(parameterHash);
    }

    private bool CrossFadeAnimatorState(int stateHash, float transition)
    {
        if (animator == null)
            return false;

        animator.CrossFadeInFixedTime(stateHash, transition, 0, 0f);
        return true;
    }

    private bool CrossFadeAnimatorState(string stateName, float transition)
    {
        if (animator == null)
            return false;

        animator.CrossFadeInFixedTime(stateName, transition, 0, 0f);
        return true;
    }
}







