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

    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
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

        public AttackTiming(int triggerHash, int damage, float startup, float activeTime, float recovery, float range, float height, float verticalOffset)
        {
            this.triggerHash = triggerHash;
            this.damage = damage;
            this.startup = startup;
            this.activeTime = activeTime;
            this.recovery = recovery;
            this.range = range;
            this.height = height;
            this.verticalOffset = verticalOffset;
        }
    }

    public void Initialize(bool controlledByPlayer, SahurFighterController enemy, int health, float groundHeight)
    {
        playerControlled = controlledByPlayer;
        opponent = enemy;
        maxHealth = health;
        CurrentHealth = maxHealth;
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

        Vector3 position = transform.position;
        position.y = groundY;
        transform.position = position;
        remoteTargetPosition = position;
        remoteTargetRotation = transform.rotation;

        animator = GetComponentInChildren<Animator>();
        SetAnimatorGrounded();
        animator?.SetFloat(VerticalVelocity, 0f);
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

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
        {
            blocking = false;
            NetworkMoveSpeed = 0f;
            animator?.SetFloat(MoveSpeed, 0f);
            animator?.SetBool(IsBlocking, false);
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
        ApplyGravity();
        UpdateAnimator(horizontal);
    }

    private float GetMovementInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return 0f;

        float input = 0f;
        if (keyboard.aKey.isPressed) input -= 1f;
        if (keyboard.dKey.isPressed) input += 1f;
        return input;
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
        Keyboard keyboard = Keyboard.current;
        blocking = keyboard != null && grounded && keyboard.sKey.isPressed;
        if (keyboard != null && blocking && keyboard.sKey.wasPressedThisFrame)
            animator?.SetTrigger(Block);
    }

    private void HandleAttackInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || Time.time < nextAttackTime || blocking || attackCoroutine != null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
            StartAttack(new AttackTiming(LowPunch, 8, 0.16f, 0.12f, 0.18f, 1.25f, 0.75f, 0.65f));
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
            StartAttack(new AttackTiming(HighPunch, 12, 0.24f, 0.14f, 0.22f, 1.55f, 0.85f, 0.95f));
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
            StartAttack(new AttackTiming(SuperPunch, 22, 0.36f, 0.18f, 0.36f, 1.9f, 1.0f, 0.85f));
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

            if (attackCoroutine == null && Time.time >= nextAttackTime && distance <= 1.9f && Random.value < aiAttackChance)
            {
                int attackIndex = Random.Range(0, 3);
                if (attackIndex == 0)
                    StartAttack(new AttackTiming(LowPunch, 8, 0.16f, 0.12f, 0.18f, 1.25f, 0.75f, 0.65f));
                else if (attackIndex == 1)
                    StartAttack(new AttackTiming(HighPunch, 12, 0.24f, 0.14f, 0.22f, 1.55f, 0.85f, 0.95f));
                else
                    StartAttack(new AttackTiming(SuperPunch, 22, 0.36f, 0.18f, 0.36f, 1.9f, 1.0f, 0.85f));
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
        nextAttackTime = Time.time + attack.startup + attack.activeTime + attack.recovery;
        attackSequence++;
        lastAttackTrigger = attack.triggerHash;
        animator?.SetTrigger(attack.triggerHash);
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
                opponent.TakeDamage(attack.damage);
                pvpNetwork?.SendDamageToRemote(attack.damage);
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

        if (CurrentHealth <= 0)
        {
            defeated = true;
            inputEnabled = false;
            PlayKnockdown();
            return;
        }

        animator?.SetTrigger(Hit);
    }

    private void PlayKnockdown()
    {
        if (animator == null)
            return;

        animator.ResetTrigger(Hit);
        animator.ResetTrigger(Jump);
        animator.SetBool(IsBlocking, false);
        animator.SetTrigger(Knockdown);
        animator.CrossFadeInFixedTime(Knockdown, 0.05f, 0, 0f);
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
                animator?.SetTrigger(Hit);
                lastRemoteHitHealth = CurrentHealth;
            }
        }

        defeated = isDefeated || CurrentHealth <= 0;

        if (animator != null)
        {
            animator.SetFloat(MoveSpeed, moveSpeed);
            animator.SetBool(IsGrounded, isGrounded);
            animator.SetFloat(VerticalVelocity, remoteVerticalVelocity);
            animator.SetBool(IsBlocking, remoteBlocking);

            if (shouldTriggerRemoteJump)
            {
                animator.ResetTrigger(Jump);
                animator.SetTrigger(Jump);
                animator.CrossFadeInFixedTime(JumpStart, 0.03f, 0, 0f);
            }

            if (shouldTriggerRemoteLanding)
            {
                animator.ResetTrigger(Jump);
                animator.CrossFadeInFixedTime(Idle, 0.05f, 0, 0f);
            }

            if (remoteAttackSequence != lastAppliedRemoteAttackSequence && remoteAttackTrigger != 0)
            {
                animator.SetTrigger(remoteAttackTrigger);
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

    private void Move(float horizontal)
    {
        Vector3 position = transform.position;
        position.x = Mathf.Clamp(position.x + horizontal * walkSpeed * Time.deltaTime, -stageLimit, stageLimit);
        transform.position = position;
    }

    private void BeginJump()
    {
        verticalVelocity = jumpForce;
        grounded = false;

        if (animator == null)
            return;

        animator.ResetTrigger(Jump);
        animator.SetBool(IsGrounded, false);
        animator.SetFloat(VerticalVelocity, verticalVelocity);
        animator.SetTrigger(Jump);
        animator.CrossFadeInFixedTime(JumpStart, 0.03f, 0, 0f);
    }

    private void Land()
    {
        verticalVelocity = 0f;
        grounded = true;

        if (animator == null)
            return;

        animator.ResetTrigger(Jump);
        animator.SetBool(IsGrounded, true);
        animator.SetFloat(VerticalVelocity, 0f);
        animator.CrossFadeInFixedTime(Idle, 0.05f, 0, 0f);
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

        animator.SetFloat(MoveSpeed, NetworkMoveSpeed);
        animator.SetBool(IsBlocking, blocking);
        animator.SetFloat(VerticalVelocity, verticalVelocity);
        SetAnimatorGrounded();
    }

    private void SetAnimatorGrounded()
    {
        if (animator != null)
            animator.SetBool(IsGrounded, grounded);
    }
}








