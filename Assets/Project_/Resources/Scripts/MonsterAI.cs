// 1. MonsterAI.cs (Gắn vào Prefab Quái - Thay thế MonsterController)
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Rigidbody))]
public class MonsterAI : MonoBehaviour
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
    public float animationDampTime = 0.1f; 
    
    private float currentSpeed;
    private bool isDead = false;

    [Header("--- Chiến Đấu ---")]
    public float attackRange = 1.5f;
    public float attackCooldown = 1.2f; 
    public float invincibilityTime = 0.5f; 
    private bool canAttack = true;
    private bool isInvincible = false;

    private Animator anim;
    private Transform heroTarget;

    void Start()
    {
        anim = GetComponent<Animator>();
        currentHP = maxHP;
        currentSpeed = runSpeed;

        GameObject hero = GameObject.FindGameObjectWithTag("Player"); 
        if (hero != null) heroTarget = hero.transform;

        int spawnType = Random.Range(0, 2);
        if (spawnType == 0) anim.SetTrigger("SpawnGround");
        else anim.SetTrigger("SpawnAir");

        StartCoroutine(SpeedDecayRoutine());
    }

    void Update()
    {
        if (isDead || heroTarget == null) return;

        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        bool isBusyWithAction = stateInfo.IsTag("Action");
        bool isTransitioningToAction = anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsTag("Action");

        if (isBusyWithAction || isTransitioningToAction)
        {
            anim.SetFloat("Speed", 0f, 0.05f, Time.deltaTime);
            return; 
        }

        float distance = Vector3.Distance(transform.position, heroTarget.position);
        
        if (distance > attackRange)
        {
            MoveTowardsHero();
        }
        else if (canAttack)
        {
            anim.SetFloat("Speed", 0f, animationDampTime, Time.deltaTime);
            anim.SetTrigger("Attack");
            StartCoroutine(AttackCooldownRoutine());
        }
    }

    private void MoveTowardsHero()
    {
        Vector3 moveDir = (heroTarget.position - transform.position).normalized;
        moveDir.y = 0f;

        if (moveDir.magnitude > 0.1f)
        {
            transform.Translate(moveDir * currentSpeed * Time.deltaTime, Space.World);
            
            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }

        anim.SetFloat("Speed", currentSpeed, animationDampTime, Time.deltaTime);
    }

    public void TakeDamage(float amount, bool isHeavyHit)
    {
        if (isDead || isInvincible) return; 

        currentHP -= amount;

        if (currentHP <= 0)
        {
            Die();
        }
        else
        {
            if (isHeavyHit) anim.SetTrigger("HitHeavy");
            else anim.SetTrigger("HitLight");

            StartCoroutine(InvincibilityRoutine());
        }
    }

    private IEnumerator SpeedDecayRoutine()
    {
        yield return new WaitForSeconds(runDuration);

        while (currentSpeed > walkSpeed && !isDead)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, walkSpeed, speedDecaySpeed * Time.deltaTime);
            yield return null; 
        }
    }

    private IEnumerator AttackCooldownRoutine()
    {
        canAttack = false;
        // Gây sát thương cho HeroBotAI ở đây
        HeroBotAI hero = heroTarget.GetComponent<HeroBotAI>();
        if(hero != null) hero.TakeDamage(10f);

        yield return new WaitForSeconds(attackCooldown); 
        canAttack = true;
    }

    private IEnumerator InvincibilityRoutine()
    {
        isInvincible = true;
        yield return new WaitForSeconds(invincibilityTime); 
        isInvincible = false;
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
        Destroy(gameObject);
    }

    private void Die()
    {
        isDead = true;
        anim.SetTrigger("Die");
        StartCoroutine(DeathRoutine());
    }
}