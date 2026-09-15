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

        float moveX = 0f;
        float moveZ = 0f;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveX += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveX -= 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveZ += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveZ -= 1f;
        }

        Vector3 moveDir = new Vector3(moveX, 0f, moveZ).normalized;

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
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (!anim.GetCurrentAnimatorStateInfo(0).IsTag("Action"))
            {
                anim.SetTrigger("Attack");
            }
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            isHoldingWand = true;
            anim.SetBool("IsHoldingWand", true);
        }
        else if (Mouse.current.rightButton.wasReleasedThisFrame)
        {
            isHoldingWand = false;
            anim.SetBool("IsHoldingWand", false);
        }
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