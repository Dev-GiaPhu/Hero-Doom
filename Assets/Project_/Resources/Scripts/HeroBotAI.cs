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
    Execution,
    HealingPulse,
    ArcaneNova,
    InfernoField,
    OrbitingSaws
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
    [Min(0.05f)] public float targetRecheckInterval = 0.28f;
    [Min(0)] public float currentTargetPreference = 2.5f;
    [Min(0f)] public float targetSwitchThreshold = 3f;

    [Header("Tactical Survival AI")]
    [Tooltip("Hero bắt đầu ưu tiên tạo khoảng trống khi HP thấp hơn mốc này và đang bị vây.")]
    [Range(0.1f, 0.9f)] public float tacticalRetreatHp = 0.58f;
    [Tooltip("Số quái ở gần để Hero xem là đang bị vây.")]
    [Range(1, 8)] public int surroundedThreatCount = 3;
    [Tooltip("Khoảng Hero cố tạo ra khi reposition.")]
    [Range(1f, 8f)] public float tacticalRepositionDistance = 3.4f;
    [Tooltip("Bán kính dùng để chấm điểm vị trí an toàn.")]
    [Range(2f, 12f)] public float tacticalDangerRadius = 6f;
    [Tooltip("Số hướng NavMesh được thử khi tìm chỗ reposition.")]
    [Range(4, 16)] public int tacticalPositionSamples = 8;

    [Header("Level Up - AI chooses one of two")]
    public HeroUpgradeThought upgradeThoughtPrefab;
    public HeroUpgradeDefinition[] upgradeDefinitions = new HeroUpgradeDefinition[16];
    [Header("Survival skill VFX - authored prefabs")]
    public HeroSkillBurst attackImpactVisualPrefab;
    public HeroSkillBurst healingPulseVisualPrefab;
    public HeroSkillBurst arcaneNovaVisualPrefab;
    public HeroFireField fireFieldPrefab;
    public HeroOrbitingSaw orbitingSawPrefab;
    public HeroSkillBurst lightningStrikeVisualPrefab;
    private HeroUpgradeThought currentThought;
    private readonly Queue<HeroUpgradeOffer> pendingUpgradeOffers = new();
    private Coroutine upgradeDecisionRoutine;

    private struct HeroUpgradeOffer
    {
        public HeroUpgradeDefinition first;
        public HeroUpgradeDefinition second;
    }

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

    private readonly int[] upgradeStacks = new int[16];
    private readonly List<HeroOrbitingSaw> orbitingSaws = new();
    private float nextInfernoField;
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
    private float nextHealingPulse;
    private float nextArcaneNova;

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
        TickSurvivalSkills();

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
        float roleThreat = monster.role switch
        {
            MonsterRole.Assassin => 13f,
            MonsterRole.Mage => 11f,
            MonsterRole.Tank => 5f,
            _ => 7f
        };

        float proximity = Mathf.Clamp01(1f - distance / Mathf.Max(1f, searchRadius)) * 15f;
        float finishValue = (1f - monster.HPPercent) * 10f;
        float objectiveThreat = monster.GuardPillar != null ? 4f : 0f;
        float dps = monster.attackDamage / Mathf.Max(0.2f, monster.attackCooldown);
        float dpsThreat = Mathf.Clamp(dps / 5f, 0f, 10f);
        float isolation = 1f - Mathf.Clamp01(BattlefieldTactics.CountAlliesNear(monster, monster.transform.position, 3.2f) / 3f);
        float isolationBonus = isolation * 4f;

        // Hero xử lý glass-cannon trước, nhưng vẫn ưu tiên mục tiêu ở đủ gần để không chạy xuyên cả bầy.
        if (monster.role == MonsterRole.Mage || monster.role == MonsterRole.Assassin)
            finishValue *= 1.25f;

        return roleThreat + proximity + finishValue + objectiveThreat + dpsThreat + isolationBonus;
    }

    private void EvaluateUtilityActions()
    {
        // EXP chỉ được nhặt trong pickup radius; Hero không đổi đường chạy chỉ để đuổi orb.
        targetExperience = null;
        targetObjective = CapturePillarManager.Instance != null
            ? CapturePillarManager.Instance.FindClosestCaptured(transform.position)
            : null;

        int nearbyThreats = CountNearbyMonsters(transform.position, tacticalDangerRadius);
        float hpRatio = currentHP / Mathf.Max(1f, maxHP);
        float danger = Mathf.Clamp01(nearbyThreats / Mathf.Max(1f, surroundedThreatCount));

        float combatScore = ScoreCombatAction() * combatWeight;
        float objectiveScore = ScoreObjectiveAction(targetObjective) * objectiveWeight;
        float idleScore = 1f;

        // Khi HP thấp hoặc đang bị vây, Hero không bỏ combat để đi pillar.
        combatScore += danger * 22f + (1f - hpRatio) * danger * 24f;
        objectiveScore *= Mathf.Lerp(1f, 0.25f, danger);
        if (hpRatio < 0.35f && nearbyThreats > 0) objectiveScore *= 0.25f;

        combatScore += currentAction == HeroUtilityAction.Combat ? currentActionInertia : 0f;
        objectiveScore += currentAction == HeroUtilityAction.AttackObjective ? currentActionInertia : 0f;
        idleScore += currentAction == HeroUtilityAction.Idle ? currentActionInertia : 0f;

        HeroUtilityAction bestAction = HeroUtilityAction.Idle;
        float bestScore = idleScore;

        ConsiderUtilityCandidate(HeroUtilityAction.Combat, combatScore, ref bestAction, ref bestScore);
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
            currentActionScore = currentAction switch
            {
                HeroUtilityAction.Combat => combatScore,
                HeroUtilityAction.AttackObjective => objectiveScore,
                _ => idleScore
            };
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
            HeroUtilityAction.CollectExperience => false,
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
                targetExperience = null;
                currentAction = HeroUtilityAction.Idle;
                nextDecisionAt = 0f;
                StopMoving();
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

        int nearbyThreats = CountNearbyMonsters(transform.position, tacticalDangerRadius);
        float hpRatio = currentHP / Mathf.Max(1f, maxHP);
        bool surrounded = nearbyThreats >= surroundedThreatCount;
        bool shouldReposition =
            surrounded &&
            (hpRatio <= tacticalRetreatHp || targetMonster.role == MonsterRole.Tank);

        if (shouldReposition && TryFindTacticalReposition(out Vector3 safePoint))
        {
            MoveTo(safePoint, 0.15f);
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

    private bool TryFindTacticalReposition(out Vector3 bestPoint)
    {
        bestPoint = transform.position;
        if (targetMonster == null) return false;

        Vector3 threatCenter = BattlefieldTactics.MonsterCentroid(transform.position, tacticalDangerRadius);
        Vector3 away = transform.position - threatCenter;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = transform.position - targetMonster.transform.position;
        if (away.sqrMagnitude < 0.01f)
            away = -transform.forward;
        away.Normalize();

        float bestScore = float.MinValue;
        int samples = Mathf.Max(4, tacticalPositionSamples);

        for (int i = 0; i < samples; i++)
        {
            float angle = (i - (samples - 1) * 0.5f) * (140f / Mathf.Max(1, samples - 1));
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
            Vector3 candidate = transform.position + direction * tacticalRepositionDistance;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.75f, NavMesh.AllAreas))
                continue;

            float nearestThreat = BattlefieldTactics.NearestMonsterDistance(hit.position, 10f);
            int crowd = CountNearbyMonsters(hit.position, 3.5f);
            float targetDistance = FlatDistance(hit.position, targetMonster.transform.position);
            float score =
                nearestThreat * 5f -
                crowd * 10f -
                Mathf.Abs(targetDistance - Mathf.Max(attackRange + 1f, 3f)) * 1.5f;

            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = hit.position;
            }
        }

        return bestScore > float.MinValue;
    }

    private void ExecuteExperienceAction()
    {
        CollectExperienceInPickupRadius();
        targetExperience = null;
        currentAction = HeroUtilityAction.Idle;
        nextDecisionAt = 0f;
        StopMoving();
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
        GameAudioController.Instance?.PlayMeleeHit();
        attackCounter++;

        CapturePillarManager pillars = CapturePillarManager.Instance;
        float factionDamage = pillars != null ? pillars.HeroDamageMultiplier : 1f;
        float levelPower = 1f + Mathf.Min(24, Mathf.Max(0, Level - 1)) * 0.055f;
        float damage = attackDamage * levelPower * factionDamage * (1f + 0.10f * upgradeStacks[(int)HeroUpgradeType.Damage]);
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

    private void TickSurvivalSkills()
    {
        TickInfernoField();
        EnsureOrbitingSaws();
        int healing = upgradeStacks[(int)HeroUpgradeType.HealingPulse];
        if (healing > 0 && Time.time >= nextHealingPulse && currentHP < maxHP)
        {
            nextHealingPulse = Time.time + Mathf.Max(3.5f, 10f - healing * 0.75f);
            float amount = Mathf.Min(maxHP - currentHP, maxHP * (0.08f + healing * 0.025f));
            currentHP += amount;
            DamagePopup.Spawn(damagePopupPrefab, transform.position + Vector3.up * 2.1f, amount, DamagePopupKind.Healing);
            HeroSkillBurst.Spawn(healingPulseVisualPrefab, transform.position + Vector3.up * 0.12f);
        }

        int nova = upgradeStacks[(int)HeroUpgradeType.ArcaneNova];
        if (nova > 0 && Time.time >= nextArcaneNova)
        {
            nextArcaneNova = Time.time + Mathf.Max(4f, 9f - nova * 0.65f);
            Trigger("Attack");
            HeroSkillBurst.Spawn(arcaneNovaVisualPrefab, transform.position + Vector3.up * 0.12f);
            DamageNearby(null, SkillDamage * (1.2f + nova * 0.3f), 7.5f + nova * 0.55f, 24);
        }
    }

    public float SkillDamage => attackDamage * (1f + Mathf.Min(24, Mathf.Max(0, Level - 1)) * 0.055f) *
                                (1f + .1f * upgradeStacks[(int)HeroUpgradeType.Damage]);

    private void TickInfernoField()
    {
        int rank = upgradeStacks[(int)HeroUpgradeType.InfernoField];
        if (rank <= 0 || fireFieldPrefab == null || Time.time < nextInfernoField) return;
        nextInfernoField = Time.time + Mathf.Max(4.5f, 9f - rank * .65f);
        MonsterAI target = PickBestMonsterTarget();
        if (target == null) return;
        Vector3 ground = target.transform.position;
        ground.y = transform.position.y;
        // A broad strike clears clustered enemies, then leaves a persistent burn zone.
        int strikeLimit = Mathf.Min(4, 1 + rank);
        int struck = 0;
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
        {
            if (monster == null || !monster.IsAlive ||
                Vector3.Distance(monster.transform.position, target.transform.position) > 4.5f) continue;
            Vector3 strikePoint = monster.transform.position;
            strikePoint.y = transform.position.y + 0.12f;
            HeroSkillBurst.Spawn(lightningStrikeVisualPrefab, strikePoint);
            monster.TakeDamage(SkillDamage * (0.28f + 0.06f * rank), this);
            if (++struck >= strikeLimit) break;
        }
        if (struck == 0) HeroSkillBurst.Spawn(lightningStrikeVisualPrefab, ground + Vector3.up * 0.12f);
        HeroFireField field = Instantiate(fireFieldPrefab, ground, Quaternion.identity);
        field.Activate(this, rank);
    }

    private void EnsureOrbitingSaws()
    {
        int rank = upgradeStacks[(int)HeroUpgradeType.OrbitingSaws];
        if (rank <= 0 || orbitingSawPrefab == null) return;
        orbitingSaws.RemoveAll(saw => saw == null);
        int wanted = Mathf.Min(5, 1 + rank);
        while (orbitingSaws.Count < wanted)
        {
            HeroOrbitingSaw saw = Instantiate(orbitingSawPrefab, transform.position, Quaternion.identity);
            saw.Initialize(this, orbitingSaws.Count, wanted);
            orbitingSaws.Add(saw);
        }
        foreach (HeroOrbitingSaw saw in orbitingSaws)
            if (saw != null) saw.SetBladeCount(wanted);
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
        CapturePillarManager pillars = CapturePillarManager.Instance;
        float pillarProtection = pillars != null ? Mathf.Max(1f, pillars.HeroHealthMultiplier) : 1f;
        float actual = Mathf.Min(currentHP, amount * (1f - effectiveArmor) / pillarProtection);
        currentHP -= actual;
        if (actual > 0f)
            HeroSkillBurst.Spawn(attackImpactVisualPrefab, transform.position + Vector3.up * 0.95f);

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

        while (Experience >= ExperienceToNext)
        {
            Experience -= ExperienceToNext;
            Level++;
            GameAudioController.Instance?.PlayReward();
            // Level growth keeps the Hero competitive through the middle of a long run.
            maxHP *= 1.055f;
            currentHP = Mathf.Min(maxHP, currentHP + maxHP * .04f);
            QueueUpgradeDecision();
        }
        HeroDoomGameManager.Instance?.SaveHeroProgression(this);
    }

    public void CopyProgressionTo(int[] stacks, out int level, out float experience)
    {
        level = Level;
        experience = Experience;
        if (stacks == null) return;
        for (int i = 0; i < Mathf.Min(stacks.Length, upgradeStacks.Length); i++) stacks[i] = upgradeStacks[i];
    }

    public void RestoreProgression(int level, float experience, int[] stacks)
    {
        Level = Mathf.Max(1, level);
        Experience = Mathf.Max(0f, experience);
        if (stacks != null)
            for (int i = 0; i < Mathf.Min(stacks.Length, upgradeStacks.Length); i++) upgradeStacks[i] = Mathf.Clamp(stacks[i], 0, 5);
        maxHP = Mathf.Max(1f, maxHP) * Mathf.Pow(1.055f, Mathf.Max(0, Level - 1)) *
                Mathf.Pow(1.12f, upgradeStacks[(int)HeroUpgradeType.MaxHealth]);
        currentHP = maxHP;
    }

    public void ResetProgression()
    {
        Level = 1;
        Experience = 0f;
        System.Array.Clear(upgradeStacks, 0, upgradeStacks.Length);
        foreach (HeroOrbitingSaw saw in orbitingSaws) if (saw != null) Destroy(saw.gameObject);
        orbitingSaws.Clear();
        StopAllCoroutines();
        pendingUpgradeOffers.Clear();
        currentThought = null;
        upgradeDecisionRoutine = null;
        nextInfernoField = Time.time + 4f;
        maxHP = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.baseHeroHP : 500f;
        currentHP = maxHP;
    }

    private void QueueUpgradeDecision()
    {
        HeroUpgradeOffer offer = BuildUpgradeOffer();
        if (offer.first == null || offer.second == null) return;
        pendingUpgradeOffers.Enqueue(offer);
        if (upgradeDecisionRoutine == null && isActiveAndEnabled)
            upgradeDecisionRoutine = StartCoroutine(ProcessUpgradeDecisions());
    }

    private HeroUpgradeOffer BuildUpgradeOffer()
    {
        List<HeroUpgradeDefinition> options = new();
        foreach (HeroUpgradeDefinition definition in upgradeDefinitions)
        {
            if (definition != null && (int)definition.type >= 0 && (int)definition.type < upgradeStacks.Length && upgradeStacks[(int)definition.type] < 5)
                options.Add(definition);
        }
        if (options.Count == 0) return default;
        if (options.Count == 1) return new HeroUpgradeOffer { first = options[0], second = options[0] };

        float[] state = BuildUpgradeDecisionState();
        Dictionary<HeroUpgradeDefinition, float> offerScores = new();
        foreach (HeroUpgradeDefinition option in options)
            offerScores[option] = ScoreUpgrade(option, state) + Random.Range(-0.16f, 0.16f);
        options.Sort((a, b) => offerScores[b].CompareTo(offerScores[a]));
        return new HeroUpgradeOffer { first = options[0], second = options[1] };
    }

    private float[] BuildUpgradeDecisionState()
    {
        float hpRatio = currentHP / Mathf.Max(1f, maxHP);
        float crowd = 0f, close = 0f, ranged = 0f, weakened = 0f;
        int nearbyCount = 0;
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
        {
            if (monster == null || !monster.IsAlive) continue;
            float distance = FlatDistance(transform.position, monster.transform.position);
            if (distance <= 8f) { crowd += 1f - distance / 10f; nearbyCount++; }
            if (distance <= 4f) close += 1f - distance / 5f;
            if (monster.role == MonsterRole.Mage && distance <= 9f) ranged += 1f - distance / 10f;
            if (distance <= 8f) weakened += Mathf.Clamp01(1f - monster.HPPercent);
        }

        float experienceUrgency = Mathf.Clamp01(Experience / Mathf.Max(1f, ExperienceToNext));
        float orbValue = 0f;
        foreach (ExperienceOrb orb in ExperienceOrb.Active)
        {
            if (orb == null || orb.IsCollected) continue;
            float distance = FlatDistance(transform.position, orb.transform.position);
            if (distance <= xpSeekRadius) orbValue = Mathf.Max(orbValue, Mathf.Clamp01(orb.value / 8f) * (1f - distance / xpSeekRadius));
        }

        float objectivePressure = targetObjective != null
            ? Mathf.Clamp01(1f - FlatDistance(transform.position, targetObjective.transform.position) / 24f)
            : 0f;
        int offensiveStacks = upgradeStacks[(int)HeroUpgradeType.Damage] + upgradeStacks[(int)HeroUpgradeType.AttackSpeed] +
                              upgradeStacks[(int)HeroUpgradeType.Cleave] + upgradeStacks[(int)HeroUpgradeType.MultiStrike] +
                              upgradeStacks[(int)HeroUpgradeType.Shockwave] + upgradeStacks[(int)HeroUpgradeType.ArcaneNova] +
                              upgradeStacks[(int)HeroUpgradeType.InfernoField] + upgradeStacks[(int)HeroUpgradeType.OrbitingSaws];

        return new[]
        {
            Mathf.Clamp01(1f - hpRatio), Mathf.Clamp01(close / 3f), Mathf.Clamp01(crowd / 5f),
            Mathf.Clamp01(ranged / 3f), nearbyCount > 0 ? Mathf.Clamp01(weakened / nearbyCount) : 0f,
            Mathf.Clamp01(experienceUrgency * 0.65f + orbValue * 0.35f), objectivePressure,
            Mathf.Clamp01(offensiveStacks / 12f)
        };
    }

    private float ScoreUpgrade(HeroUpgradeDefinition definition, float[] state)
    {
        int rank = upgradeStacks[(int)definition.type];
        float relevance = definition.utility != null ? definition.utility.Score(state) : 0f;
        float lowHealth = state.Length > 0 ? state[0] : 0f;
        float closePressure = state.Length > 1 ? state[1] : 0f;
        float crowdPressure = state.Length > 2 ? state[2] : 0f;
        float rangedPressure = state.Length > 3 ? state[3] : 0f;
        float weakenedEnemies = state.Length > 4 ? state[4] : 0f;
        float offenseBuilt = state.Length > 7 ? state[7] : 0f;

        float tacticalSynergy = definition.type switch
        {
            HeroUpgradeType.MaxHealth => lowHealth * 1.35f + crowdPressure * .35f,
            HeroUpgradeType.Armor => lowHealth * 1.15f + closePressure * .55f,
            HeroUpgradeType.LifeSteal => lowHealth * .95f + offenseBuilt * .35f,
            HeroUpgradeType.HealingPulse => lowHealth * 1.25f + crowdPressure * .3f,
            HeroUpgradeType.MoveSpeed => rangedPressure * .7f + crowdPressure * .45f,
            HeroUpgradeType.Knockback => crowdPressure * .85f + closePressure * .5f,
            HeroUpgradeType.Cleave => crowdPressure * 1.15f,
            HeroUpgradeType.Shockwave => crowdPressure * 1.2f,
            HeroUpgradeType.ArcaneNova => crowdPressure * 1.25f,
            HeroUpgradeType.InfernoField => crowdPressure * .9f + rangedPressure * .35f,
            HeroUpgradeType.OrbitingSaws => closePressure * .95f + crowdPressure * .55f,
            HeroUpgradeType.Execution => weakenedEnemies * .95f,
            HeroUpgradeType.MultiStrike => offenseBuilt * .45f + weakenedEnemies * .35f,
            HeroUpgradeType.Damage => .25f + offenseBuilt * .25f,
            HeroUpgradeType.AttackSpeed => .2f + offenseBuilt * .3f,
            HeroUpgradeType.PickupRange => .12f,
            _ => 0f
        };

        float diminishingReturn = Mathf.Lerp(1f, 0.42f, rank / 5f);
        return (relevance + tacticalSynergy) * diminishingReturn + (5 - rank) * 0.03f;
    }

    private IEnumerator ProcessUpgradeDecisions()
    {
        while (pendingUpgradeOffers.Count > 0)
        {
            HeroUpgradeOffer offer = pendingUpgradeOffers.Dequeue();
            float thinkingDuration = upgradeThoughtPrefab != null ? upgradeThoughtPrefab.thinkingDuration : 1.25f;
            if (currentThought != null) Destroy(currentThought.gameObject);
            currentThought = upgradeThoughtPrefab != null
                ? Instantiate(upgradeThoughtPrefab, transform.position + Vector3.up * 3.1f, Quaternion.identity)
                : null;
            currentThought?.Show(transform, offer.first, offer.second, thinkingDuration);
            yield return new WaitForSecondsRealtime(thinkingDuration);

            HeroUpgradeDefinition selected = ScoreUpgrade(offer.first, BuildUpgradeDecisionState()) + Random.Range(-0.12f, 0.12f) >=
                                             ScoreUpgrade(offer.second, BuildUpgradeDecisionState()) + Random.Range(-0.12f, 0.12f)
                ? offer.first : offer.second;
            ApplyUpgrade(selected.type);
            currentThought?.Resolve(selected, upgradeStacks[(int)selected.type]);
            HeroDoomGameManager.Instance?.SaveHeroProgression(this);
            float resultDuration = currentThought != null ? currentThought.resultDuration : 0.6f;
            yield return new WaitForSecondsRealtime(resultDuration);
            currentThought = null;
        }
        upgradeDecisionRoutine = null;
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
        if (type == HeroUpgradeType.OrbitingSaws) EnsureOrbitingSaws();
        HeroDoomGameManager.Instance?.SaveHeroProgression(this);
    }

    public float HealFraction(float fraction)
    {
        if (!IsAlive || fraction <= 0f) return 0f;
        float healed = Mathf.Min(maxHP - currentHP, maxHP * fraction);
        currentHP += healed;
        return healed;
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
        if (agent == null) return false;
        if (agent.enabled && agent.isOnNavMesh) return true;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 8f, NavMesh.AllAreas))
        {
            if (agent.enabled) agent.enabled = false;
            transform.position = hit.position;
            agent.enabled = true;
            return agent.isOnNavMesh || agent.Warp(hit.position);
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

