using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum MonsterRole
{
    BasicMelee,
    Mage,
    Tank,
    Assassin
}

public class MonsterAI : MonoBehaviour
{
    public static readonly HashSet<MonsterAI> ActiveMonsters = new();

    [Header("Role")]
    public MonsterRole role = MonsterRole.BasicMelee;

    [Header("Stats")]
    public float maxHP = 130f;
    public float currentHP;
    public float moveSpeed = 4.2f;
    public float attackDamage = 14f;
    public float attackRange = 1.5f;
    public float attackCooldown = 1f;
    [Range(0f, 0.8f)] public float armor = 0f;

    [Header("Mage")]
    public float preferredRange = 6f;
    public float retreatRange = 3.5f;
    public float specialCooldown = 7f;
    public float specialDamage = 28f;

    [Header("Combat Feel")]
    public float rotationSpeed = 12f;
    public float knockbackForce = 0.7f;
    public float corpseHoldTime = 2.5f;
    public float shrinkDuration = 1.25f;

    public bool IsAlive { get; private set; } = true;
    public float HPPercent => maxHP <= 0f ? 0f : currentHP / maxHP;
    public CapturePillar GuardPillar { get; private set; }

    private HeroBotAI heroTarget;
    private Animator anim;
    private NavMeshAgent agent;
    private Rigidbody rb;
    private float attackTimer;
    private float specialTimer;
    private bool beingKnocked;

    void Awake()
    {
        EnsureRuntimeComponents();
        currentHP = maxHP;
    }

    void OnEnable()
    {
        if (IsAlive) ActiveMonsters.Add(this);
    }

    void OnDisable()
    {
        ActiveMonsters.Remove(this);
        if (GuardPillar != null) GuardPillar.UnregisterGuard(this);
    }

    void Start()
    {
        HeroBotAI currentHero = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.CurrentHero : FindFirstObjectByType<HeroBotAI>();
        heroTarget = currentHero;
        TryPlaceOnNavMesh();
        Trigger("SpawnGround");
    }

    public void ConfigureBySlot(int slot)
    {
        role = (MonsterRole)Mathf.Clamp(slot, 0, 3);
        switch (role)
        {
            case MonsterRole.Mage:
                maxHP = 90f; moveSpeed = 3.8f; attackDamage = 12f; attackRange = 8f;
                attackCooldown = 1.35f; preferredRange = 6f; retreatRange = 3.2f;
                specialCooldown = 7f; specialDamage = 30f; armor = 0.05f; knockbackForce = 0.5f;
                break;
            case MonsterRole.Tank:
                maxHP = 320f; moveSpeed = 2.6f; attackDamage = 18f; attackRange = 1.7f;
                attackCooldown = 1.35f; armor = 0.32f; knockbackForce = 1.2f;
                break;
            case MonsterRole.Assassin:
                maxHP = 75f; moveSpeed = 7f; attackDamage = 25f; attackRange = 1.45f;
                attackCooldown = 0.65f; armor = 0f; knockbackForce = 0.85f;
                break;
            default:
                maxHP = 130f; moveSpeed = 4.2f; attackDamage = 14f; attackRange = 1.55f;
                attackCooldown = 1f; armor = 0.08f; knockbackForce = 0.65f;
                break;
        }

        currentHP = maxHP;
        EnsureRuntimeComponents();
        ApplyAgentSettings();
    }

    void Update()
    {
        if (!IsAlive || beingKnocked) return;

        attackTimer -= Time.deltaTime;
        specialTimer -= Time.deltaTime;

        if (heroTarget == null || !heroTarget.IsAlive)
            heroTarget = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.CurrentHero : FindFirstObjectByType<HeroBotAI>();

        if (heroTarget == null || !heroTarget.IsAlive)
        {
            StopMoving();
            return;
        }

        if (!TryPlaceOnNavMesh()) return;

        if (GuardPillar != null)
        {
            HandleGuardBehaviour();
            return;
        }

        HandleRoleBehaviour();
    }

    private void HandleRoleBehaviour()
    {
        float distance = FlatDistance(transform.position, heroTarget.transform.position);

        if (role == MonsterRole.Mage)
        {
            if (distance < retreatRange)
            {
                Vector3 away = (transform.position - heroTarget.transform.position).normalized;
                MoveTo(transform.position + away * preferredRange);
                return;
            }

            if (distance > preferredRange + 1f)
            {
                MoveTo(heroTarget.transform.position);
                return;
            }

            StopMoving();
            Face(heroTarget.transform.position);

            if (specialTimer <= 0f && (HPPercent < 0.55f || distance <= preferredRange))
            {
                CastMageSpecial();
                return;
            }

            if (attackTimer <= 0f) AttackHero(false);
            return;
        }

        if (distance > attackRange)
        {
            MoveTo(heroTarget.transform.position);
            return;
        }

        StopMoving();
        Face(heroTarget.transform.position);
        if (attackTimer <= 0f) AttackHero(role == MonsterRole.Tank);
    }

    private void HandleGuardBehaviour()
    {
        if (GuardPillar == null) return;

        float heroDistance = FlatDistance(transform.position, heroTarget.transform.position);
        bool heroNearZone = GuardPillar.IsInside(heroTarget.transform.position, attackRange + 1.5f);

        if (heroNearZone && heroDistance <= Mathf.Max(attackRange, role == MonsterRole.Mage ? preferredRange + 1f : attackRange + 2f))
        {
            if (role == MonsterRole.Mage)
            {
                if (heroDistance < retreatRange)
                {
                    Vector3 away = (transform.position - heroTarget.transform.position).normalized;
                    MoveTo(ClampToGuardZone(transform.position + away * preferredRange));
                }
                else if (heroDistance > preferredRange + 1f)
                {
                    MoveTo(ClampToGuardZone(heroTarget.transform.position));
                }
                else
                {
                    StopMoving();
                    Face(heroTarget.transform.position);
                    if (specialTimer <= 0f && (HPPercent < 0.55f || heroDistance <= preferredRange)) CastMageSpecial();
                    else if (attackTimer <= 0f) AttackHero(false);
                }
            }
            else if (heroDistance > attackRange) MoveTo(ClampToGuardZone(heroTarget.transform.position));
            else
            {
                StopMoving();
                Face(heroTarget.transform.position);
                if (attackTimer <= 0f) AttackHero(role == MonsterRole.Tank);
            }
            return;
        }

        float fromCenter = FlatDistance(transform.position, GuardPillar.transform.position);
        if (fromCenter > Mathf.Max(0.8f, GuardPillar.captureRadius * 0.55f))
            MoveTo(GuardPillar.transform.position);
        else
            StopMoving();
    }

    private Vector3 ClampToGuardZone(Vector3 desired)
    {
        Vector3 center = GuardPillar.transform.position;
        Vector3 flat = desired - center;
        flat.y = 0f;
        float max = GuardPillar.captureRadius * 0.85f;
        if (flat.magnitude > max) flat = flat.normalized * max;
        return center + flat;
    }

    private void AttackHero(bool heavy)
    {
        if (heroTarget == null || !heroTarget.IsAlive) return;
        attackTimer = Mathf.Max(0.1f, attackCooldown);
        Trigger("Attack");

        float damage = attackDamage;
        if (role == MonsterRole.Assassin && heroTarget.CurrentHP / Mathf.Max(1f, heroTarget.MaxHP) < 0.35f)
            damage *= 1.2f;

        Vector3 push = (heroTarget.transform.position - transform.position).normalized;
        if (role == MonsterRole.Mage)
            MagicProjectile.Spawn(transform.position + Vector3.up * 1.25f, heroTarget, this, damage, knockbackForce);
        else
            heroTarget.TakeDamage(damage, this, push * knockbackForce, heavy);
    }

    private void CastMageSpecial()
    {
        specialTimer = Mathf.Max(1f, specialCooldown);
        Trigger("Attack");

        if (HPPercent < 0.55f)
        {
            float heal = maxHP * 0.18f;
            currentHP = Mathf.Min(maxHP, currentHP + heal);
        }

        if (heroTarget != null && heroTarget.IsAlive && FlatDistance(transform.position, heroTarget.transform.position) <= attackRange + 1.5f)
        {
            MagicProjectile.SpawnLightning(transform.position + Vector3.up * 1.35f, heroTarget.transform.position + Vector3.up);
            Vector3 push = (heroTarget.transform.position - transform.position).normalized;
            heroTarget.TakeDamage(specialDamage, this, push * (knockbackForce + 0.8f), true);
        }
    }

    public float TakeDamage(float amount, bool heavyHit)
    {
        if (!IsAlive || amount <= 0f) return 0f;

        if (role == MonsterRole.Tank && Random.value < 0.22f)
            amount *= 0.45f;

        float actual = Mathf.Min(currentHP, amount * (1f - armor));
        currentHP -= actual;

        if (currentHP <= 0f) Die();
        else Trigger(heavyHit ? "HitHeavy" : "HitLight");

        return actual;
    }

    public void ApplyKnockback(Vector3 velocity, float duration = 0.28f)
    {
        if (!IsAlive || velocity.sqrMagnitude < 0.01f) return;
        StartCoroutine(KnockbackRoutine(velocity, duration));
    }

    private IEnumerator KnockbackRoutine(Vector3 velocity, float duration)
    {
        beingKnocked = true;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;

        InfiniteMapGenerator map = FindFirstObjectByType<InfiniteMapGenerator>();
        float time = 0f;
        while (time < duration && IsAlive)
        {
            float damping = 1f - time / duration;
            Vector3 next = transform.position + velocity * damping * Time.deltaTime;
            if (map != null) next = map.ClampInsideWorld(next);
            transform.position = next;
            if (map != null && map.IsWaterAtWorldPosition(transform.position))
            {
                KillByEnvironment();
                break;
            }
            time += Time.deltaTime;
            yield return null;
        }

        beingKnocked = false;
        TryPlaceOnNavMesh();
    }

    public void KillByEnvironment()
    {
        if (!IsAlive) return;
        currentHP = 0f;
        Die();
    }

    private void Die()
    {
        if (!IsAlive) return;
        IsAlive = false;
        ActiveMonsters.Remove(this);

        if (GuardPillar != null)
        {
            GuardPillar.UnregisterGuard(this);
            GuardPillar = null;
        }

        StopMoving();
        if (agent != null) agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        Trigger("Die");
        ExperienceOrb.Spawn(transform.position, GetXPValue());
        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(corpseHoldTime);
        Vector3 startScale = transform.localScale;
        float t = 0f;
        while (t < shrinkDuration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, Mathf.Clamp01(t / shrinkDuration));
            yield return null;
        }
        Destroy(gameObject);
    }

    public void AssignGuard(CapturePillar pillar)
    {
        if (!IsAlive) return;
        if (GuardPillar != null && GuardPillar != pillar) GuardPillar.UnregisterGuard(this);
        GuardPillar = pillar;
    }

    public void ClearGuard(CapturePillar pillar)
    {
        if (GuardPillar == pillar) GuardPillar = null;
    }

    private float GetXPValue()
    {
        switch (role)
        {
            case MonsterRole.Tank: return 8f;
            case MonsterRole.Mage: return 6f;
            case MonsterRole.Assassin: return 7f;
            default: return 4f;
        }
    }

    private void EnsureRuntimeComponents()
    {
        anim = GetComponent<Animator>();
        if (anim == null)
        {
            anim = gameObject.AddComponent<Animator>();
            RuntimeAnimatorController sharedController = Resources.Load<RuntimeAnimatorController>("Animator/Skeleton_Mage");
            if (sharedController != null) anim.runtimeAnimatorController = sharedController;
        }
        else if (anim.runtimeAnimatorController == null)
        {
            RuntimeAnimatorController sharedController = Resources.Load<RuntimeAnimatorController>("Animator/Skeleton_Mage");
            if (sharedController != null) anim.runtimeAnimatorController = sharedController;
        }

        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1f, 0f);
            capsule.height = 2f;
            capsule.radius = 0.45f;
        }

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
        ApplyAgentSettings();
    }

    private void ApplyAgentSettings()
    {
        if (agent == null) return;
        agent.speed = moveSpeed;
        agent.angularSpeed = 720f;
        agent.acceleration = Mathf.Max(10f, moveSpeed * 5f);
        agent.stoppingDistance = Mathf.Max(0.2f, role == MonsterRole.Mage ? preferredRange : attackRange * 0.85f);
        agent.radius = 0.42f;
        agent.height = 2f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    }

    private bool TryPlaceOnNavMesh()
    {
        if (agent == null || !agent.enabled) return false;
        if (agent.isOnNavMesh) return true;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
            return true;
        }
        return false;
    }

    private void MoveTo(Vector3 destination)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = false;
        agent.speed = moveSpeed;
        agent.SetDestination(destination);
        SetSpeed(moveSpeed);
    }

    private void StopMoving()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        SetSpeed(0f);
    }

    private void Face(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), rotationSpeed * Time.deltaTime);
    }

    private void Trigger(string parameter)
    {
        if (anim != null && anim.runtimeAnimatorController != null) anim.SetTrigger(parameter);
    }

    private void SetSpeed(float speed)
    {
        if (anim != null && anim.runtimeAnimatorController != null) anim.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
