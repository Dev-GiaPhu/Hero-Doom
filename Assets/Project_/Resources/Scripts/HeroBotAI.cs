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

public enum HeroUtilityAction
{
    Idle,
    Combat,
    CollectExperience,
    AttackObjective
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

    [Header("Experience")]
    [Tooltip("EXP inside this radius is collected automatically.")]
    [Min(0.1f)] public float basePickupRadius = 1.8f;
    [Tooltip("How often nearby EXP is collected.")]
    [Range(0.02f, 0.5f)] public float xpPickupCheckInterval = 0.08f;
    [Tooltip("Maximum distance at which EXP becomes a utility-AI goal.")]
    [Min(1f)] public float xpSeekRadius = 18f;
    [Tooltip("Extra utility for EXP when the Hero is close to leveling.")]
    [Range(0f, 40f)] public float xpLevelUrgencyWeight = 20f;

    [Header("Combat Feedback - authored prefabs")]
    public DamagePopup damagePopupPrefab;
    public SpawnProtection spawnProtection;
    public CombatKnockback knockbackMotor;
    [Min(0)] public float knockbackForce = 7f;
    [Min(0)] public float areaKnockbackForce = 9f;
    [Header("Utility AI")]
    [Tooltip("How often the Hero reevaluates all available actions.")]
    [Min(0.05f)] public float decisionInterval = 0.25f;
    [Tooltip("Minimum time the Hero commits to an action before a normal switch is allowed.")]
    [Min(0f)] public float minimumActionCommitment = 0.8f;
    [Tooltip("A new action must beat the current action by this amount to interrupt it after commitment.")]
    [Min(0f)] public float actionSwitchThreshold = 7f;
    [Tooltip("Utility bonus for continuing the current action. Prevents indecisive thrashing.")]
    [Min(0f)] public float currentActionInertia = 10f;
    [Tooltip("How much combat matters relative to other goals.")]
    [Range(0f, 3f)] public float combatWeight = 1f;
    [Tooltip("How much collecting EXP matters relative to other goals.")]
    [Range(0f, 3f)] public float experienceWeight = 1f;
    [Tooltip("How much attacking captured objectives matters relative to other goals.")]
    [Range(0f, 3f)] public float objectiveWeight = 0.75f;

    [Header("Combat Target Selection")]
    [Min(0.05f)] public float targetRecheckInterval = 0.35f;
    [Min(0)] public float currentTargetPreference = 2f;
    [Min(0f)] public float targetSwitchThreshold = 2.5f;

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
    public int UpgradeCount => upgradeStacks.Length;

    public int GetUpgradeStack(HeroUpgradeType type)
    {
        int index = (int)type;
        return index >= 0 && index < upgradeStacks.Length ? upgradeStacks[index] : 0;
    }

    public HeroUpgradeDefinition GetUpgradeDefinition(HeroUpgradeType type)
    {
        if (upgradeDefinitions == null) return null;
        return System.Array.Find(upgradeDefinitions, d => d != null && d.type == type);
    }

    private readonly int[] upgradeStacks = new int[12];
    private readonly List<ExperienceOrb> xpCollectBuffer = new(16);
    private MonsterAI targetMonster;
    private ExperienceOrb targetExperience;
    private CapturePillar targetObjective;
    [SerializeField] private HeroUtilityAction currentAction = HeroUtilityAction.Idle;
    [SerializeField] private float currentActionScore;
    private float nextDecisionAt;
    private float actionCommittedUntil;
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
        if (targetMonster == null || !targetMonster.IsAlive || targetRecheckTimer <= 0f)
        {
            targetMonster = PickBestMonsterTarget();
            targetRecheckTimer = Mathf.Max(0.05f, targetRecheckInterval);
        }

        if (Time.time >= nextDecisionAt || !IsCurrentActionValid())
        {
            EvaluateUtilityActions();
            nextDecisionAt = Time.time + Mathf.Max(0.05f, decisionInterval);
        }

        ExecuteCurrentAction();
    }

    private void OnMonsterDied(MonsterAI deadMonster)
    {
        if (deadMonster == null || targetMonster != deadMonster)
            return;

        targetMonster = null;
        attackTimer = 0f;
        nextDecisionAt = 0f;

        // Re-evaluate immediately instead of continuing toward the corpse.
        StopMoving();
    }

    public void SetPermanentDifficulty(float permanentMaxHP)
    {
        maxHP = Mathf.Max(1f, permanentMaxHP);
        currentHP = maxHP;
    }

    public void RestoreProgression(int level, float experience, int[] stacks)
    {
        Level = Mathf.Max(1, level);
        Experience = Mathf.Max(0f, experience);

        for (int i = 0; i < upgradeStacks.Length; i++)
            upgradeStacks[i] = stacks != null && i < stacks.Length ? Mathf.Clamp(stacks[i], 0, 5) : 0;

        // Reapply permanent effects stored in stacks after a fresh prefab instance is spawned.
        int maxHealthStacks = upgradeStacks[(int)HeroUpgradeType.MaxHealth];
        if (maxHealthStacks > 0)
        {
            float baseMax = maxHP;
            for (int i = 0; i < maxHealthStacks; i++)
                baseMax *= 1.12f;
            maxHP = baseMax;
            currentHP = maxHP;
        }
    }

    private MonsterAI PickBestMonsterTarget()
    {
        MonsterAI best = null;
        float bestScore = float.MinValue;
        float currentScore = float.MinValue;

        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
        {
            if (monster == null || !monster.IsAlive) continue;
            float distance = FlatDistance(transform.position, monster.transform.position);
            if (distance > searchRadius) continue;

            float score = ScoreMonsterTarget(monster, distance);
            if (monster == targetMonster) currentScore = score + currentTargetPreference;

            if (score > bestScore)
            {
                bestScore = score;
                best = monster;
            }
        }

        if (targetMonster != null && targetMonster.IsAlive && currentScore > float.MinValue &&
            best != targetMonster && bestScore < currentScore + targetSwitchThreshold)
            return targetMonster;

        return best;
    }

    private float ScoreMonsterTarget(MonsterAI monster, float distance)
    {
        float threat = monster.role switch
        {
            MonsterRole.Assassin => 9f,
            MonsterRole.Mage => 8f,
            MonsterRole.Tank => 4.5f,
            _ => 5.5f
        };

        float proximity = Mathf.Clamp01(1f - distance / Mathf.Max(1f, searchRadius)) * 12f;
        float finishValue = (1f - monster.HPPercent) * 7f;
        float objectiveThreat = monster.GuardPillar != null ? 5f : 0f;
        return threat + proximity + finishValue + objectiveThreat;
    }

    private void EvaluateUtilityActions()
    {
        targetExperience = PickBestExperienceTarget(out float xpScore);
        targetObjective = CapturePillarManager.Instance != null
            ? CapturePillarManager.Instance.FindClosestCaptured(transform.position)
            : null;

        float combatScore = ScoreCombatAction() * combatWeight;
        float experienceScore = xpScore * experienceWeight;
        float objectiveScore = ScoreObjectiveAction(targetObjective) * objectiveWeight;
        float idleScore = 1f;

        combatScore += currentAction == HeroUtilityAction.Combat ? currentActionInertia : 0f;
        experienceScore += currentAction == HeroUtilityAction.CollectExperience ? currentActionInertia : 0f;
        objectiveScore += currentAction == HeroUtilityAction.AttackObjective ? currentActionInertia : 0f;
        idleScore += currentAction == HeroUtilityAction.Idle ? currentActionInertia : 0f;

        HeroUtilityAction bestAction = HeroUtilityAction.Idle;
        float bestScore = idleScore;

        ConsiderUtilityCandidate(HeroUtilityAction.Combat, combatScore, ref bestAction, ref bestScore);
        ConsiderUtilityCandidate(HeroUtilityAction.CollectExperience, experienceScore, ref bestAction, ref bestScore);
        ConsiderUtilityCandidate(HeroUtilityAction.AttackObjective, objectiveScore, ref bestAction, ref bestScore);

        bool currentInvalid = !IsCurrentActionValid();
        bool commitmentExpired = Time.time >= actionCommittedUntil;
        bool clearlyBetter = bestScore >= currentActionScore + actionSwitchThreshold;

        if (bestAction != currentAction && (currentInvalid || (commitmentExpired && clearlyBetter)))
        {
            currentAction = bestAction;
            currentActionScore = bestScore;
            actionCommittedUntil = Time.time + Mathf.Max(0f, minimumActionCommitment);
        }
        else
        {
            currentActionScore = ScoreAction(currentAction, combatScore, experienceScore, objectiveScore, idleScore);
        }
    }

    private static void ConsiderUtilityCandidate(
        HeroUtilityAction candidate,
        float score,
        ref HeroUtilityAction bestAction,
        ref float bestScore)
    {
        if (score <= bestScore) return;
        bestScore = score;
        bestAction = candidate;
    }

    private float ScoreAction(
        HeroUtilityAction action,
        float combatScore,
        float experienceScore,
        float objectiveScore,
        float idleScore)
    {
        return action switch
        {
            HeroUtilityAction.Combat => combatScore,
            HeroUtilityAction.CollectExperience => experienceScore,
            HeroUtilityAction.AttackObjective => objectiveScore,
            _ => idleScore
        };
    }

    private float ScoreCombatAction()
    {
        if (targetMonster == null || !targetMonster.IsAlive) return 0f;

        float distance = FlatDistance(transform.position, targetMonster.transform.position);
        float hpRatio = currentHP / Mathf.Max(1f, maxHP);
        float immediateThreat = Mathf.Clamp01(1f - distance / 9f);
        float score = 28f + immediateThreat * 28f;

        if (targetMonster.role == MonsterRole.Assassin) score += 10f;
        if (targetMonster.role == MonsterRole.Mage) score += 7f;
        if (hpRatio < 0.35f) score += immediateThreat * 15f;

        return score;
    }

    private ExperienceOrb PickBestExperienceTarget(out float bestUtility)
    {
        ExperienceOrb best = null;
        bestUtility = 0f;

        float need = Mathf.Clamp01(1f - Experience / Mathf.Max(1f, ExperienceToNext));
        float closeToLevel = 1f - need;
        float hpRatio = currentHP / Mathf.Max(1f, maxHP);

        foreach (ExperienceOrb orb in ExperienceOrb.Active)
        {
            if (orb == null || orb.IsCollected) continue;

            float distance = FlatDistance(transform.position, orb.transform.position);
            if (distance > xpSeekRadius) continue;

            float distanceUtility = Mathf.Clamp01(1f - distance / Mathf.Max(1f, xpSeekRadius));
            float valueUtility = Mathf.Clamp01(orb.value / 8f);
            float dangerPenalty = CountNearbyMonsters(orb.transform.position, 4f) * 5f;
            float lowHealthPenalty = hpRatio < 0.3f ? (0.3f - hpRatio) * 35f : 0f;

            float utility =
                12f +
                distanceUtility * 24f +
                valueUtility * 18f +
                closeToLevel * xpLevelUrgencyWeight -
                dangerPenalty -
                lowHealthPenalty;

            if (orb == targetExperience) utility += 5f;

            if (utility > bestUtility)
            {
                bestUtility = utility;
                best = orb;
            }
        }

        return best;
    }

    private int CountNearbyMonsters(Vector3 point, float radius)
    {
        int count = 0;
        float radiusSqr = radius * radius;
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
        {
            if (monster == null || !monster.IsAlive) continue;
            Vector3 delta = monster.transform.position - point;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSqr) count++;
        }
        return count;
    }

    private float ScoreObjectiveAction(CapturePillar objective)
    {
        if (objective == null) return 0f;
        float distance = FlatDistance(transform.position, objective.transform.position);
        return 18f + Mathf.Clamp01(1f - distance / 28f) * 20f;
    }

    private bool IsCurrentActionValid()
    {
        return currentAction switch
        {
            HeroUtilityAction.Combat => targetMonster != null && targetMonster.IsAlive,
            HeroUtilityAction.CollectExperience => targetExperience != null && !targetExperience.IsCollected,
            HeroUtilityAction.AttackObjective => targetObjective != null,
            _ => true
        };
    }

    private void ExecuteCurrentAction()
    {
        switch (currentAction)
        {
            case HeroUtilityAction.Combat:
                ExecuteCombatAction();
                break;
            case HeroUtilityAction.CollectExperience:
                ExecuteExperienceAction();
                break;
            case HeroUtilityAction.AttackObjective:
                ExecuteObjectiveAction();
                break;
            default:
                StopMoving();
                break;
        }
    }

    private void ExecuteCombatAction()
    {
        if (targetMonster == null || !targetMonster.IsAlive)
        {
            StopMoving();
            return;
        }

        float distance = FlatDistance(transform.position, targetMonster.transform.position);
        if (distance > attackRange)
        {
            MoveTo(targetMonster.transform.position);
            return;
        }

        StopMoving();
        Face(targetMonster.transform.position);
        if (attackTimer <= 0f) AttackTarget();
    }

    private void ExecuteExperienceAction()
    {
        if (targetExperience == null || targetExperience.IsCollected)
        {
            StopMoving();
            return;
        }

        float pickupRadius =
            basePickupRadius *
            (1f + 0.2f * upgradeStacks[(int)HeroUpgradeType.PickupRange]);

        if (FlatDistance(transform.position, targetExperience.transform.position) <= pickupRadius)
        {
            targetExperience.Collect(this);
            targetExperience = null;
            nextDecisionAt = 0f;
            return;
        }

        MoveTo(targetExperience.transform.position, Mathf.Max(0.2f, pickupRadius * 0.65f));
    }

    private void ExecuteObjectiveAction()
    {
        if (targetObjective == null)
        {
            StopMoving();
            return;
        }

        MoveTo(targetObjective.transform.position);
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

            // Normal monster hits should not continuously shove the Hero around.
            // Heavy attacks still apply explicit knockback and can throw the Hero into water.
            if (heavyHit && knockback.sqrMagnitude > 0.01f)
                knockbackMotor?.Push(knockback);
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
        HeroDoomGameManager.Instance?.SaveHeroProgression(this);

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

            // Small noise keeps the Hero from producing the exact same build every run
            // while still strongly preferring upgrades that answer the current battlefield.
            score += Random.Range(-0.35f, 0.35f);

            if (score > bestScore) { bestScore = score; best = option; }
        }
        ApplyUpgrade(best);
        HeroDoomGameManager.Instance?.SaveHeroProgression(this);
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
        int nearbyMelee = 0;
        int nearbyMage = 0;
        int nearbyTank = 0;
        int nearbyAssassin = 0;

        foreach (MonsterAI m in MonsterAI.ActiveMonsters)
        {
            if (m == null || !m.IsAlive) continue;

            float distance = FlatDistance(transform.position, m.transform.position);
            if (distance > 7f) continue;

            nearby++;
            switch (m.role)
            {
                case MonsterRole.Mage: nearbyMage++; break;
                case MonsterRole.Tank: nearbyTank++; break;
                case MonsterRole.Assassin: nearbyAssassin++; break;
                default: nearbyMelee++; break;
            }
        }

        switch (type)
        {
            case HeroUpgradeType.MaxHealth: return 5f + (1f - hpRatio) * 7f + nearbyAssassin * 0.8f;
            case HeroUpgradeType.Damage: return 6f + nearbyTank * 1.8f;
            case HeroUpgradeType.AttackSpeed: return 5.8f + nearbyTank * 1.2f;
            case HeroUpgradeType.MoveSpeed: return 3.8f + nearbyMage * 0.9f;
            case HeroUpgradeType.Armor: return 4.5f + nearbyMelee * 0.8f + nearbyAssassin * 1.4f;
            case HeroUpgradeType.PickupRange: return 3.2f;
            case HeroUpgradeType.Cleave: return 2.5f + nearby * 1.45f;
            case HeroUpgradeType.Knockback: return 3f + nearbyMelee * 0.8f + nearbyAssassin * 1.1f;
            case HeroUpgradeType.LifeSteal: return 2.8f + (1f - hpRatio) * 9f;
            case HeroUpgradeType.MultiStrike: return 5f + nearbyTank * 0.8f + nearby * 0.25f;
            case HeroUpgradeType.Shockwave: return 2f + nearby * 1.75f;
            case HeroUpgradeType.Execution: return 4.6f + nearbyTank * 0.7f;
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

    private void MoveTo(Vector3 destination, float stoppingDistance = -1f)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        float speedBonus = 1f + 0.06f * upgradeStacks[(int)HeroUpgradeType.MoveSpeed];
        agent.speed = moveSpeed * speedBonus;
        agent.stoppingDistance = stoppingDistance >= 0f ? stoppingDistance : attackRange * 0.8f;
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
