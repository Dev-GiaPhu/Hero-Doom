using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum HeroUpgradeType
{
    MaxHealth,
    Damage,
    AttackSpeed,
    MoveSpeed,
    Armor,
    PickupRange,
    Cleave,
    Knockback,
    LifeSteal,
    MultiStrike,
    Shockwave,
    Execution
}

public class HeroBotAI : MonoBehaviour
{
    [Header("Base Stats")]
    public float maxHP = 500f;
    public float currentHP;
    public float moveSpeed = 4f;
    public float rotationSpeed = 15f;
    public float attackDamage = 20f;
    public float attackRange = 2f;
    public float attackCooldown = 1f;
    public float searchRadius = 24f;
    [Range(0f, 0.8f)] public float armor = 0.05f;

    [Header("Survivor Auto Pickup")]
    [Tooltip("EXP trong bán kính này được tự động nhặt khi Hero đi ngang qua. Hero không đổi đường để chạy tới EXP.")]
    [Min(0.1f)] public float basePickupRadius = 1.8f;
    [Tooltip("Khoảng thời gian giữa các lần kiểm tra EXP trong vùng nhặt.")]
    [Range(0.02f, 0.5f)] public float xpPickupCheckInterval = 0.08f;

    [Header("Combat Feedback - authored prefabs")]
    public DamagePopup damagePopupPrefab;
    public SpawnProtection spawnProtection;
    public CombatKnockback knockbackMotor;
    [Min(0)] public float knockbackForce = 7f;
    [Min(0)] public float areaKnockbackForce = 9f;
    [Header("Target Selection")]
    [Min(0.05f)] public float targetRecheckInterval = 0.35f;
    [Min(0)] public float currentTargetPreference = 2f;

    [Header("Level Up - AI chooses one of two")]
    public HeroUpgradeThought upgradeThoughtPrefab;
    public HeroUpgradeDefinition[] upgradeDefinitions = new HeroUpgradeDefinition[12];
    private HeroUpgradeThought currentThought;

    [Header("Death")]
    public float corpseHoldTime = 2.5f;
    public float shrinkDuration = 1.25f;

    public bool IsAlive { get; private set; } = true;
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public int Level { get; private set; } = 1;
    public float Experience { get; private set; }
    public float ExperienceToNext => 10f + (Level - 1) * 8f;

    private readonly int[] upgradeStacks = new int[12];
    private readonly List<ExperienceOrb> xpCollectBuffer = new(16);
    private MonsterAI targetMonster;
    private Animator anim;
    private NavMeshAgent agent;
    private Rigidbody rb;
    private float attackTimer;
    private float xpPickupTimer;
    private float targetRecheckTimer;
    private int attackCounter;

    void Awake()
    {
        EnsureRuntimeComponents();
        currentHP = maxHP;
    }

    void OnEnable()
    {
        MonsterAI.Died += OnMonsterDied;
    }

    void OnDisable()
    {
        MonsterAI.Died -= OnMonsterDied;
    }

    void Start()
    {
        HeroDoomGameManager.Instance?.RegisterHero(this);
        TryPlaceOnNavMesh();
    }

    void Update()
    {
        if (!IsAlive) return;
        if (knockbackMotor != null && knockbackMotor.IsActive) { SetSpeed(0); return; }

        attackTimer -= Time.deltaTime;
        xpPickupTimer -= Time.deltaTime;

        if (xpPickupTimer <= 0f)
        {
            xpPickupTimer = Mathf.Max(0.02f, xpPickupCheckInterval);
            CollectExperienceInPickupRadius();
        }

        if (!TryPlaceOnNavMesh()) return;

        targetRecheckTimer -= Time.deltaTime;
        if (targetMonster == null || !targetMonster.IsAlive || targetRecheckTimer <= 0)
        {
            targetMonster = PickBestMonsterTarget();
            targetRecheckTimer = Mathf.Max(0.05f, targetRecheckInterval);
        }

        if (targetMonster != null && targetMonster.IsAlive)
        {
            float distance = FlatDistance(transform.position, targetMonster.transform.position);
            if (distance > attackRange)
            {
                MoveTo(targetMonster.transform.position);
            }
            else
            {
                StopMoving();
                Face(targetMonster.transform.position);
                if (attackTimer <= 0f) AttackTarget();
            }
            return;
        }

        CapturePillar captured = CapturePillarManager.Instance != null
            ? CapturePillarManager.Instance.FindClosestCaptured(transform.position) : null;

        if (captured != null)
        {
            MoveTo(captured.transform.position);
            return;
        }

        StopMoving();
    }

    private void OnMonsterDied(MonsterAI deadMonster)
    {
        if (deadMonster == null || targetMonster != deadMonster)
            return;

        targetMonster = null;
        attackTimer = 0f;

        // Không tiếp tục nhìn/chạy vào xác đang phát animation chết.
        StopMoving();
    }

    public void SetPermanentDifficulty(float permanentMaxHP)
    {
        maxHP = Mathf.Max(1f, permanentMaxHP);
        currentHP = maxHP;
    }

    private MonsterAI PickBestMonsterTarget()
    {
        MonsterAI best = null;
        float bestScore = float.MinValue;

        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
        {
            if (monster == null || !monster.IsAlive) continue;
            float distance = FlatDistance(transform.position, monster.transform.position);
            if (distance > searchRadius) continue;

            float threat;
            switch (monster.role)
            {
                case MonsterRole.Assassin: threat = 7f; break;
                case MonsterRole.Mage: threat = 6f; break;
                case MonsterRole.Tank: threat = 3.5f; break;
                default: threat = 4f; break;
            }

            float score = threat + Mathf.Max(0f, 8f - distance) * 0.7f;
            score += (1f - monster.HPPercent) * 2.5f;
            if (monster.GuardPillar != null) score += 5f;

            if (monster == targetMonster) score += currentTargetPreference;

            if (score > bestScore)
            {
                bestScore = score;
                best = monster;
            }
        }

        return best;
    }

    private void CollectExperienceInPickupRadius()
    {
        float pickupRadius =
            basePickupRadius *
            (1f + 0.2f * upgradeStacks[(int)HeroUpgradeType.PickupRange]);

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        xpCollectBuffer.Clear();

        foreach (ExperienceOrb orb in ExperienceOrb.Active)
        {
            if (orb == null || orb.IsCollected)
                continue;

            Vector3 delta = orb.transform.position - transform.position;
            delta.y = 0f;

            if (delta.sqrMagnitude <= pickupRadiusSqr)
                xpCollectBuffer.Add(orb);
        }

        for (int i = 0; i < xpCollectBuffer.Count; i++)
        {
            ExperienceOrb orb = xpCollectBuffer[i];

            if (orb != null)
                orb.Collect(this);
        }

        xpCollectBuffer.Clear();
    }

    private void AttackTarget()
    {
        if (targetMonster == null || !targetMonster.IsAlive) return;

        float cooldownScale = 1f - 0.07f * upgradeStacks[(int)HeroUpgradeType.AttackSpeed];
        attackTimer = Mathf.Max(0.18f, attackCooldown * Mathf.Max(0.45f, cooldownScale));
        Trigger("Attack");
        attackCounter++;

        float damage = attackDamage * (1f + 0.10f * upgradeStacks[(int)HeroUpgradeType.Damage]);
        if (upgradeStacks[(int)HeroUpgradeType.Execution] > 0 && targetMonster.HPPercent < 0.25f)
            damage *= 1f + 0.10f * upgradeStacks[(int)HeroUpgradeType.Execution];

        MonsterAI attackedMonster = targetMonster;
        float actual = attackedMonster.TakeDamage(damage, false);

        float knock = knockbackForce + 1.2f * upgradeStacks[(int)HeroUpgradeType.Knockback];
        Vector3 push = (attackedMonster.transform.position - transform.position).normalized;

        if (attackedMonster.IsAlive && actual > 0)
            attackedMonster.ApplyKnockback(push * knock);
        else if (targetMonster == attackedMonster)
            targetMonster = null;

        int lifeSteal = upgradeStacks[(int)HeroUpgradeType.LifeSteal];
        if (lifeSteal > 0 && actual > 0f)
        {
            float heal = Mathf.Min(maxHP - currentHP, actual * (0.03f * lifeSteal));
            currentHP += heal;
            DamagePopup.Spawn(damagePopupPrefab, transform.position + Vector3.up * 2.1f, heal, DamagePopupKind.Healing);
        }

        int cleave = upgradeStacks[(int)HeroUpgradeType.Cleave];
        if (cleave > 0) DamageNearby(targetMonster, damage * (0.18f + 0.04f * cleave), attackRange + 1.3f, 3);

        int multi = upgradeStacks[(int)HeroUpgradeType.MultiStrike];
        if (multi > 0 && Random.value < 0.08f * multi)
            DamageNearby(targetMonster, damage * 0.65f, attackRange + 2f, 1);

        int shock = upgradeStacks[(int)HeroUpgradeType.Shockwave];
        if (shock > 0 && attackCounter % Mathf.Max(2, 7 - shock) == 0)
            DamageNearby(null, damage * 0.28f, 3.5f, 8);
    }

    private void DamageNearby(MonsterAI excluded, float damage, float radius, int maxTargets)
    {
        int hit = 0;
        foreach (MonsterAI monster in new List<MonsterAI>(MonsterAI.ActiveMonsters))
        {
            if (monster == null || !monster.IsAlive || monster == excluded) continue;
            if (FlatDistance(transform.position, monster.transform.position) > radius) continue;
            float actual = monster.TakeDamage(damage, true);
            Vector3 push = (monster.transform.position - transform.position).normalized;
            if (actual > 0) monster.ApplyKnockback(push * areaKnockbackForce);
            if (++hit >= maxTargets) break;
        }
    }

    public float TakeDamage(float amount, MonsterAI source, Vector3 knockback, bool heavyHit)
    {
        if (!IsAlive || amount <= 0f || (spawnProtection != null && spawnProtection.IsProtected)) return 0f;

        float effectiveArmor = Mathf.Clamp01(armor + 0.04f * upgradeStacks[(int)HeroUpgradeType.Armor]);
        float actual = Mathf.Min(currentHP, amount * (1f - effectiveArmor));
        currentHP -= actual;

        DamagePopup.Spawn(
            damagePopupPrefab,
            transform.position + Vector3.up * 2.05f,
            actual,
            DamagePopupKind.DamageToHero,
            heavyHit);

        HeroDoomGameManager.Instance?.AddDamageGold(actual);

        if (currentHP <= 0f)
        {
            Die();
        }
        else
        {
            Trigger(heavyHit ? "HitHeavy" : "HitLight");
            if (knockback.sqrMagnitude > 0.01f) knockbackMotor?.Push(knockback);
        }

        return actual;
    }

    public void TakeDamage(float amount)
    {
        TakeDamage(amount, null, Vector3.zero, false);
    }

    public void AddExperience(float amount)
    {
        if (!IsAlive || amount <= 0f) return;
        Experience += amount;

        while (Experience >= ExperienceToNext)
        {
            Experience -= ExperienceToNext;
            Level++;
            ChooseUpgradeAutomatically();
        }
    }

    private void ChooseUpgradeAutomatically()
    {
        List<HeroUpgradeType> candidates = new();
        for (int i = 0; i < upgradeStacks.Length; i++)
            if (upgradeStacks[i] < 5) candidates.Add((HeroUpgradeType)i);
        if (candidates.Count == 0) return;

        List<HeroUpgradeType> offered = new();
        while (offered.Count < Mathf.Min(2, candidates.Count))
        {
            HeroUpgradeType pick = candidates[Random.Range(0, candidates.Count)];
            if (!offered.Contains(pick)) offered.Add(pick);
        }

        HeroUpgradeType best = offered[0];
        float bestScore = float.MinValue;
        foreach (HeroUpgradeType option in offered)
        {
            float score = UpgradeUtility(option);
            if (monster == targetMonster) score += currentTargetPreference;

            if (score > bestScore) { bestScore = score; best = option; }
        }
        ApplyUpgrade(best);
        HeroUpgradeDefinition definition = System.Array.Find(upgradeDefinitions, d => d != null && d.type == best);
        if (upgradeThoughtPrefab != null && definition != null)
        {
            if (currentThought != null) Destroy(currentThought.gameObject);
            currentThought = Instantiate(upgradeThoughtPrefab, transform.position + Vector3.up * 3.1f, Quaternion.identity);
            currentThought.Show(transform, definition, upgradeStacks[(int)best]);
        }
    }

    private float UpgradeUtility(HeroUpgradeType type)
    {
        float hpRatio = currentHP / Mathf.Max(1f, maxHP);
        int nearby = 0;
        foreach (MonsterAI m in MonsterAI.ActiveMonsters)
            if (m != null && m.IsAlive && FlatDistance(transform.position, m.transform.position) < 5f) nearby++;

        switch (type)
        {
            case HeroUpgradeType.MaxHealth: return 5f + (1f - hpRatio) * 5f;
            case HeroUpgradeType.Damage: return 7f;
            case HeroUpgradeType.AttackSpeed: return 6.5f;
            case HeroUpgradeType.MoveSpeed: return 4f;
            case HeroUpgradeType.Armor: return 5f + nearby;
            case HeroUpgradeType.PickupRange: return 3.5f;
            case HeroUpgradeType.Cleave: return 3f + nearby * 1.4f;
            case HeroUpgradeType.Knockback: return 3.5f + nearby;
            case HeroUpgradeType.LifeSteal: return 3f + (1f - hpRatio) * 8f;
            case HeroUpgradeType.MultiStrike: return 5.5f + nearby * 0.5f;
            case HeroUpgradeType.Shockwave: return 2.5f + nearby * 1.8f;
            case HeroUpgradeType.Execution: return 5f;
            default: return 1f;
        }
    }

    private void ApplyUpgrade(HeroUpgradeType type)
    {
        int index = (int)type;
        if (upgradeStacks[index] >= 5) return;
        upgradeStacks[index]++;

        if (type == HeroUpgradeType.MaxHealth)
        {
            float add = maxHP * 0.12f;
            maxHP += add;
            currentHP += add;
        }
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
        spawnProtection?.End();
        if (currentThought != null) Destroy(currentThought.gameObject);
        if (knockbackMotor != null) knockbackMotor.enabled = false;
        StopMoving();
        if (agent != null) agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Trigger("Die");

        HeroDoomGameManager.Instance?.NotifyHeroDied(this);
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

    private void EnsureRuntimeComponents()
    {
        anim = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
        agent = GetComponent<NavMeshAgent>();
        if (spawnProtection == null) spawnProtection = GetComponent<SpawnProtection>();
        if (knockbackMotor == null) knockbackMotor = GetComponent<CombatKnockback>();
        if (agent == null) Debug.LogError("Hero prefab needs an authored NavMeshAgent.", this);
    }

    private bool TryPlaceOnNavMesh()
    {
        if (agent == null || !agent.enabled) return false;
        if (agent.isOnNavMesh) return true;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
            return true;
        }
        return false;
    }

    private void MoveTo(Vector3 destination)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        float speedBonus = 1f + 0.06f * upgradeStacks[(int)HeroUpgradeType.MoveSpeed];
        agent.speed = moveSpeed * speedBonus;
        agent.stoppingDistance = attackRange * 0.8f;
        agent.isStopped = false;
        agent.SetDestination(destination);
        SetSpeed(agent.speed);
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
