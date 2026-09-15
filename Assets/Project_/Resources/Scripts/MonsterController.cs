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
    private float runTimer;
    private bool isDead = false;
    private bool isHoldingWand = false;

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

        int spawnType = Random.Range(0, 2);
        if (spawnType == 0)
            anim.SetTrigger("SpawnGround");
        else
            anim.SetTrigger("SpawnAir");

        currentSpeed = runSpeed;
        runTimer = runDuration;
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
        if (runTimer > 0)
        {
            runTimer -= Time.deltaTime;
        }
        else
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, walkSpeed, speedDecaySpeed * Time.deltaTime);
        }

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
        if (attackAction.triggered)
        {
            if (!anim.GetCurrentAnimatorStateInfo(0).IsTag("Action"))
            {
                anim.SetTrigger("Attack");
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

    public void TakeDamage(float amount, bool isHeavyHit)
    {
        if (isDead) return;

        currentHP -= amount;

        if (currentHP <= 0)
        {
            Die();
        }
        else
        {
            if (isHeavyHit) anim.SetTrigger("HitHeavy");
            else anim.SetTrigger("HitLight");
        }
    }

    private void Die()
    {
        isDead = true;
        anim.SetTrigger("Die");
        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(3f);

        float shrinkDuration = 1.5f;
        Vector3 originalScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < shrinkDuration)
        {
            transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, elapsed / shrinkDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localScale = Vector3.zero;
        Destroy(gameObject);
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