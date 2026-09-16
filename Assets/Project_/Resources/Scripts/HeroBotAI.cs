// 3. HeroBotAI.cs (Gắn vào mô hình Hero của bạn)
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Rigidbody))]
public class HeroBotAI : MonoBehaviour
{
    [Header("--- Chỉ Số Hero ---")]
    public float maxHP = 500f;
    public float currentHP;
    public float moveSpeed = 4f;
    public float rotationSpeed = 15f;
    
    [Header("--- Chiến Đấu ---")]
    public float attackRange = 2f;
    public float attackCooldown = 1f;
    public float searchRadius = 20f;
    
    private Transform targetMonster;
    private Animator anim;
    private bool isDead = false;
    private bool canAttack = true;

    void Start()
    {
        anim = GetComponent<Animator>();
        currentHP = maxHP;
    }

    void Update()
    {
        if (isDead) return;

        FindClosestMonster();

        if (targetMonster != null)
        {
            float distance = Vector3.Distance(transform.position, targetMonster.position);
            if (distance > attackRange)
            {
                MoveTowardsTarget();
            }
            else if (canAttack)
            {
                anim.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
                anim.SetTrigger("Attack");
                StartCoroutine(AttackCooldownRoutine());
            }
        }
        else
        {
            anim.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
        }
    }

    private void FindClosestMonster()
    {
        GameObject[] monsters = GameObject.FindGameObjectsWithTag("Enemy");
        float closestDist = searchRadius;
        targetMonster = null;

        foreach (var mon in monsters)
        {
            float dist = Vector3.Distance(transform.position, mon.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                targetMonster = mon.transform;
            }
        }
    }

    private void MoveTowardsTarget()
    {
        Vector3 moveDir = (targetMonster.position - transform.position).normalized;
        moveDir.y = 0f;
        
        transform.Translate(moveDir * moveSpeed * Time.deltaTime, Space.World);
        
        if (moveDir.magnitude > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
        }
        
        anim.SetFloat("Speed", moveSpeed, 0.1f, Time.deltaTime);
    }

    public void TakeDamage(float amount)
    {
        if (isDead) return;

        currentHP -= amount;
        if (currentHP <= 0)
        {
            Die();
        }
        else
        {
            anim.SetTrigger("HitLight");
        }
    }

    private IEnumerator AttackCooldownRoutine()
    {
        canAttack = false;
        if (targetMonster != null)
        {
            MonsterAI mon = targetMonster.GetComponent<MonsterAI>();
            if (mon != null) mon.TakeDamage(20f, false);
        }
        yield return new WaitForSeconds(attackCooldown);
        canAttack = true;
    }

    private void Die()
    {
        isDead = true;
        anim.SetTrigger("Die");
        StartCoroutine(ReviveRoutine());
    }

    private IEnumerator ReviveRoutine()
    {
        yield return new WaitForSeconds(3f);
        isDead = false;
        currentHP = maxHP;
        anim.SetTrigger("Revive");
    }
}