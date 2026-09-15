using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Rigidbody))]
public class MonsterController : MonoBehaviour
{
    [Header("--- Chỉ Số Cơ Bản ---")]
    public float maxHP = 100f;
    public float currentHP;

    [Header("--- Di Chuyển ---")]
    public float runSpeed = 8f;
    public float walkSpeed = 3f;
    public float runDuration = 3f; 
    public float speedDecaySpeed = 2f; 
    public float rotationSpeed = 15f; 
    
    private float currentSpeed;
    private bool isDead = false;
    private bool isHoldingWand = false;

    [Header("--- Chiến Đấu ---")]
    public float attackCooldown = 1.2f; // Thời gian chờ giữa 2 lần đánh
    public float invincibilityTime = 0.5f; // Thời gian bất tử sau khi bị dính đòn
    private bool canAttack = true;
    private bool isInvincible = false;

    [Header("--- Test Animation Triggers ---")]
    public bool testPickup = false;
    public bool testHitLight = false;
    public bool testHitHeavy = false;
    public float testDamage = 20f;

    private Animator anim;
    
    private InputAction moveAction;
    private InputAction attackAction;
    private InputAction wandAction;
    private InputAction interactAction;

    void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value);
        
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        attackAction = new InputAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
        wandAction = new InputAction("Wand", InputActionType.Button, "<Mouse>/rightButton");
        interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
    }

    void OnEnable()
    {
        moveAction.Enable();
        attackAction.Enable();
        wandAction.Enable();
        interactAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        attackAction.Disable();
        wandAction.Disable();
        interactAction.Disable();
    }

    void Start()
    {
        anim = GetComponent<Animator>();
        currentHP = maxHP;
        currentSpeed = runSpeed;

        int spawnType = Random.Range(0, 2);
        if (spawnType == 0) anim.SetTrigger("SpawnGround");
        else anim.SetTrigger("SpawnAir");

        // Gọi Coroutine xử lý giảm tốc độ ngay khi vừa sinh ra
        StartCoroutine(SpeedDecayRoutine());
    }

    void Update()
    {
        if (isDead) return;

        HandleTestTriggers();
        HandleCombatInputs();
        HandleMovement();
    }

    private void HandleMovement()
    {
        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        Vector3 moveDir = new Vector3(moveInput.x, 0f, moveInput.y).normalized;

        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        bool isBusyWithAction = stateInfo.IsTag("Action");

        if (isHoldingWand || isBusyWithAction)
        {
            anim.SetFloat("Speed", 0f);
            return; 
        }

        if (moveDir.magnitude > 0.1f)
        {
            transform.Translate(moveDir * currentSpeed * Time.deltaTime, Space.World);
            
            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
            
            anim.SetFloat("Speed", currentSpeed);
        }
        else
        {
            anim.SetFloat("Speed", 0f);
        }
    }

    private void HandleCombatInputs()
    {
        if (attackAction.triggered && canAttack)
        {
            if (!anim.GetCurrentAnimatorStateInfo(0).IsTag("Action"))
            {
                anim.SetTrigger("Attack");
                StartCoroutine(AttackCooldownRoutine()); // Kích hoạt hồi chiêu
            }
        }

        if (interactAction.triggered)
        {
            if (!anim.GetCurrentAnimatorStateInfo(0).IsTag("Action"))
            {
                anim.SetTrigger("Interact");
            }
        }

        isHoldingWand = wandAction.ReadValue<float>() > 0.5f;
        anim.SetBool("IsHoldingWand", isHoldingWand);
    }

    public void TakeDamage(float amount, bool isHeavyHit)
    {
        if (isDead || isInvincible) return; // Không nhận sát thương nếu đang bất tử

        currentHP -= amount;

        if (currentHP <= 0)
        {
            Die();
        }
        else
        {
            if (isHeavyHit) anim.SetTrigger("HitHeavy");
            else anim.SetTrigger("HitLight");

            // Kích hoạt thời gian bất tử ngắn sau khi bị dính đòn
            StartCoroutine(InvincibilityRoutine());
        }
    }

    // ==========================================
    // HỆ THỐNG COROUTINE
    // ==========================================

    private IEnumerator SpeedDecayRoutine()
    {
        // 1. Chờ hết thời gian chạy nhanh
        yield return new WaitForSeconds(runDuration);

        // 2. Bắt đầu giảm tốc độ từ từ cho đến khi bằng walkSpeed
        while (currentSpeed > walkSpeed && !isDead)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, walkSpeed, speedDecaySpeed * Time.deltaTime);
            yield return null; // Đợi đến frame tiếp theo
        }
    }

    private IEnumerator AttackCooldownRoutine()
    {
        canAttack = false;
        yield return new WaitForSeconds(attackCooldown); // Chờ hết hồi chiêu
        canAttack = true;
    }

    private IEnumerator InvincibilityRoutine()
    {
        isInvincible = true;
        yield return new WaitForSeconds(invincibilityTime); // Chờ hết thời gian bất tử
        isInvincible = false;
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(3f); // Chờ ngã xuống và nằm yên

        float shrinkDuration = 1.5f;
        Vector3 originalScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < shrinkDuration)
        {
            transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, elapsed / shrinkDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }

    private void Die()
    {
        isDead = true;
        anim.SetTrigger("Die");
        StartCoroutine(DeathRoutine());
    }

    // ==========================================
    // CÁC HÀM TIỆN ÍCH KHÁC
    // ==========================================

    private void HandleTestTriggers()
    {
        if (testPickup)
        {
            anim.SetTrigger("Pickup");
            testPickup = false;
        }

        if (testHitLight)
        {
            TakeDamage(testDamage, false);
            testHitLight = false;
        }

        if (testHitHeavy)
        {
            TakeDamage(testDamage * 2f, true);
            testHitHeavy = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Item") && !isDead)
        {
            anim.SetTrigger("Pickup");
            Destroy(other.gameObject);
        }
    }
}