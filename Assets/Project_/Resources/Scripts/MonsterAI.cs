using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;

public enum MonsterRole
{
    BasicMelee,
    Mage,
    Tank,
    Assassin
}

public enum MonsterUtilityAction
{
    Idle,
    Advance,
    Attack,
    Retreat,
    MageSpecial,
    ReturnToGuard,
    CollectGold,
    MineGold
}

public class MonsterAI : MonoBehaviour
{
    public static readonly HashSet<MonsterAI> ActiveMonsters = new();
    public static event System.Action<MonsterAI> Died;

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
    [Tooltip("Prefab used for normal magic attacks. Leave empty to use the default sphere.")]
    public GameObject mageProjectilePrefab;
    public float preferredRange = 6f;
    public float retreatRange = 3.5f;
    public float specialCooldown = 7f;
    public float specialDamage = 28f;

    [Header("Mage Decisions")]
    [Min(0.1f)] public float retreatDuration = 0.65f;
    [Min(0.1f)] public float retreatCooldown = 2.5f;
    [Min(0.1f)] public float retreatDistance = 2.5f;
    [Min(0.1f)] public float castStandTime = 0.45f;
    [Min(0.1f)] public float rangeHysteresis = 0.75f;
    public LineRenderer lightningPrefab;

    [Header("Combat Feedback - authored prefabs")]
    public DamagePopup damagePopupPrefab;
    public ExperienceOrb experienceOrbPrefab;
    public SpawnProtection spawnProtection;
    public CombatKnockback knockbackMotor;
    public HeroSkillBurst hitEffectPrefab;

    [Header("Utility AI")]
    [Min(0.05f)] public float decisionInterval = 0.22f;
    [Min(0f)] public float minimumActionCommitment = 0.45f;
    [Min(0f)] public float actionSwitchThreshold = 6f;
    [Min(0f)] public float currentActionInertia = 8f;

    [Header("Tactical Cooperation")]
    [Tooltip("Preferred separation from friendly monsters. Formation slots and retreat use this to avoid clumping.")]
    [Range(0.6f, 4f)] public float allySpacing = 1.6f;
    [Tooltip("How strongly this role follows its formation position around the Hero.")]
    [Range(0f, 2f)] public float formationWeight = 0.9f;
    [Tooltip("Assassin disengages after a bad dive below this HP ratio.")]
    [Range(0.05f, 0.6f)] public float assassinDisengageHp = 0.26f;
    [Tooltip("Mage prefers retreat points close enough to a Tank or the allied formation.")]
    [Range(0f, 3f)] public float mageAnchorWeight = 1.15f;
    [Tooltip("Extra aggression when allies are already pressuring the Hero.")]
    [Range(0f, 2f)] public float teamPressureWeight = 1f;

    [Header("Autonomous Gold Economy")]
    [Tooltip("Cho phép quái tự đi nhặt vàng và khai thác, cả khi Hero đang chết.")]
    public bool canGatherGold = true;
    [Tooltip("Độ ưu tiên kinh tế của loại quái này. Tank nên thấp, Basic nên cao.")]
    [Range(0f, 2.5f)] public float economicMotivation = 1f;
    [Tooltip("Hero ở trong vùng này sẽ bị xem như nguy hiểm / vật cản.")]
    [Min(1f)] public float heroInterventionRadius = 10f;
    [Tooltip("Khoảng cách tối đa AI tìm mỏ và đồng vàng.")]
    [Min(4f)] public float economicSearchRadius = 35f;
    [Tooltip("Vàng trong ví ít hơn ngưỡng này khiến AI kiếm tiền tích cực hơn.")]
    [Min(1f)] public float lowGoldThreshold = 300f;
    [Tooltip("Độ ưu tiên cắt ngang mining khi Hero đến gần. Cao hơn = sẵn sàng đánh.")]
    [Range(0f, 3f)] public float defensiveAggression = 1f;
    [Tooltip("Khoảng thời gian thực hiện một lượt khai thác. 0 = dùng workDuration của mỏ.")]
    [Min(0f)] public float customWorkInterval = 0f;
    [Tooltip("Bán kính quái tự nhặt vàng đang ở ngay bên cạnh mà không cần pathfind.")]
    [Range(0.5f, 3f)] public float goldAutoPickupRadius = 1.35f;
    [Tooltip("Khoảng thời gian quét nhanh vàng sát chân.")]
    [Range(0.05f, 0.5f)] public float goldAutoPickupInterval = .14f;

    [Header("Coordinated Tactical AI")]
    [Tooltip("After being wounded, prefer defending or fighting over continued harvesting.")]
    [Min(0.5f)] public float heroThreatMemorySeconds = 3.5f;
    [Tooltip("Extra urgency for engaging the Hero after being attacked.")]
    [Range(0f, 40f)] public float defensiveResponseWeight = 20f;
    [Tooltip("How strongly roles take positions based on the Hero's movement direction.")]
    [Range(0f, 2f)] public float dynamicFormationWeight = 1f;

    [Header("Combat Feel")]
    public float rotationSpeed = 12f;
    public float knockbackForce = 0.7f;
    public float corpseHoldTime = 2.5f;
    public float shrinkDuration = 1.25f;

    public bool IsAlive { get; private set; } = true;
    public float HPPercent => maxHP <= 0f ? 0f : currentHP / maxHP;
    public CapturePillar GuardPillar { get; private set; }
    public bool IsPlayerControlled { get; private set; }
    public MonsterUtilityAction CurrentTacticalAction => currentUtilityAction;
    // Expose live movement only; other AI systems do not need access to NavMeshAgent internals.
    public Vector3 CurrentVelocity => agent != null && agent.enabled && agent.isOnNavMesh
        ? agent.velocity : Vector3.zero;
    private Vector3 playerMoveTarget;
    private bool hasPlayerMoveTarget;

    private float appliedHealthMultiplier = 1;
    private float appliedDamageMultiplier = 1;
    private HeroBotAI heroTarget;
    private Animator anim;
    private NavMeshAgent agent;
    private Rigidbody rb;
    private float attackTimer;
    private float specialTimer;
    private float retreatEndsAt;
    private float nextRetreatAt;
    private float castEndsAt;
    private bool retreating;
    private bool approaching;
    private Vector3 retreatDestination;
    private float nextPathUpdate;
    private Vector3 lastDestination;
    private NavMeshPath retreatPath;
    [SerializeField] private MonsterUtilityAction currentUtilityAction = MonsterUtilityAction.Idle;
    [SerializeField] private float currentUtilityScore;
    private float nextDecisionAt;
    private float actionCommittedUntil;
    private Vector3 lastHeroPosition;
    private Vector3 heroVelocityEstimate;
    private GoldMine targetGoldMine;
    private GoldPickup targetGoldPickup;
    private float miningProgress;
    private float nextGoldPickupAt;
    private NavMeshPath economicPath;
    private float lastHeroThreatAt = -999f;
    public GoldMine AssignedGoldMine => targetGoldMine;

    void Awake()
    {
        retreatPath = new NavMeshPath();
        economicPath = new NavMeshPath();
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
        ReleaseGoldTasks();
        if (GuardPillar != null) GuardPillar.UnregisterGuard(this);
    }

    void Start()
    {
        HeroBotAI currentHero = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.CurrentHero : FindFirstObjectByType<HeroBotAI>();
        heroTarget = currentHero;
        if (heroTarget != null)
            lastHeroPosition = heroTarget.transform.position;
        TryPlaceOnNavMesh();
        Trigger("SpawnGround");
    }

    private float appliedMoveSpeedMultiplier = 1f;
    private float appliedAttackSpeedMultiplier = 1f;

    public void ApplySummonerUpgrade(float healthMultiplier, float damageMultiplier, float moveSpeedMultiplier = 1f, float attackSpeedMultiplier = 1f)
    {
        float hpRatio = currentHP / Mathf.Max(1, maxHP);
        maxHP = maxHP / appliedHealthMultiplier * healthMultiplier;
        currentHP = maxHP * hpRatio;
        attackDamage = attackDamage / appliedDamageMultiplier * damageMultiplier;
        specialDamage = specialDamage / appliedDamageMultiplier * damageMultiplier;
        moveSpeed = moveSpeed / Mathf.Max(.01f, appliedMoveSpeedMultiplier) * Mathf.Max(.01f, moveSpeedMultiplier);
        attackCooldown = attackCooldown * Mathf.Max(.01f, appliedAttackSpeedMultiplier) / Mathf.Max(.01f, attackSpeedMultiplier);
        appliedHealthMultiplier = healthMultiplier;
        appliedDamageMultiplier = damageMultiplier;
        appliedMoveSpeedMultiplier = moveSpeedMultiplier;
        appliedAttackSpeedMultiplier = attackSpeedMultiplier;
        ApplyAgentSettings();
    }

    [System.Obsolete("Monster stats are authored on each prefab. Configure prefab values in Inspector.")]
    public void ConfigureBySlot(int slot)
    {
        role = (MonsterRole)Mathf.Clamp(slot, 0, 3);
        ApplyAgentSettings();
    }

    void Update()
    {
        if (!IsAlive) return;
        if (knockbackMotor != null && knockbackMotor.IsActive) { SetSpeed(0); return; }

        attackTimer -= Time.deltaTime;
        specialTimer -= Time.deltaTime;

        if (heroTarget == null || !heroTarget.IsAlive)
        {
            HeroBotAI livingHero = HeroDoomGameManager.Instance != null
                ? HeroDoomGameManager.Instance.CurrentHero
                : null;
            if (livingHero != null && livingHero.IsAlive)
            {
                heroTarget = livingHero;
                lastHeroPosition = heroTarget.transform.position;
                heroVelocityEstimate = Vector3.zero;
                nextDecisionAt = 0f;
            }
            else
                heroTarget = null;
        }

        if (!TryPlaceOnNavMesh()) return;

        if (Time.time >= nextGoldPickupAt)
        {
            nextGoldPickupAt = Time.time + Mathf.Max(.05f, goldAutoPickupInterval);
            CollectGoldAtFeet();
        }

        if (heroTarget != null) UpdateHeroPrediction();

        // Direct F-control always overrides economic planning, while manual walking
        // still collects coins that happen to be under the monster.
        if (IsPlayerControlled)
        {
            if (targetGoldMine != null || targetGoldPickup != null) ReleaseGoldTasks();

            if (heroTarget != null && heroTarget.IsAlive &&
                FlatDistance(transform.position, heroTarget.transform.position) <= attackRange &&
                attackTimer <= 0f)
            {
                StopMoving();
                Face(heroTarget.transform.position);
                AttackHero(role == MonsterRole.Tank);
            }
            else if (hasPlayerMoveTarget && FlatDistance(transform.position, playerMoveTarget) > .55f)
                MoveTo(playerMoveTarget, .35f);
            else
            {
                hasPlayerMoveTarget = false;
                StopMoving();
            }
            return;
        }

        if (Time.time >= nextDecisionAt || !IsCurrentUtilityActionValid())
        {
            EvaluateUtilityAction();
            nextDecisionAt = Time.time + Mathf.Max(.05f, decisionInterval);
        }

        ExecuteUtilityAction();
    }

    private void UpdateHeroPrediction()
    {
        if (heroTarget == null) return;
        Vector3 current = heroTarget.transform.position;
        heroVelocityEstimate = Vector3.Lerp(heroVelocityEstimate, (current - lastHeroPosition) / Mathf.Max(.01f, Time.deltaTime), .18f);
        heroVelocityEstimate.y = 0f;
        lastHeroPosition = current;
    }

    private Vector3 PredictedHeroPosition(float leadSeconds)
    {
        return heroTarget == null ? transform.position : heroTarget.transform.position + Vector3.ClampMagnitude(heroVelocityEstimate, 8f) * leadSeconds;
    }
    private void EvaluateUtilityAction()
    {
        bool heroPresent = heroTarget != null && heroTarget.IsAlive;
        float distance = heroPresent
            ? FlatDistance(transform.position, heroTarget.transform.position)
            : float.PositiveInfinity;
        bool hitRecently = Time.time - lastHeroThreatAt <= Mathf.Max(.5f, heroThreatMemorySeconds);
        bool wasEngaged = currentUtilityAction == MonsterUtilityAction.Attack ||
                          currentUtilityAction == MonsterUtilityAction.Advance ||
                          currentUtilityAction == MonsterUtilityAction.MageSpecial;
        bool heroThreatensThisUnit = heroPresent &&
            (distance <= heroInterventionRadius ||
             (hitRecently && distance <= heroInterventionRadius + 6f) ||
             (wasEngaged && distance <= heroInterventionRadius * 1.25f));

        float attackScore = heroPresent ? ScoreAttack(distance) : 0f;
        float advanceScore = heroPresent ? ScoreAdvance(distance) : 0f;
        float retreatScore = heroPresent ? ScoreRetreat(distance) : 0f;
        float specialScore = heroPresent ? ScoreMageSpecial(distance) : 0f;
        float guardScore = ScoreReturnToGuard();
        float idleScore = 1f;

        // Damage memory prevents miners switching straight back to farming while
        // under attack, without forcing units across the entire map.
        if (hitRecently && heroPresent && distance <= heroInterventionRadius + 6f)
        {
            float urgency = defensiveResponseWeight *
                            Mathf.Clamp01(1f - (Time.time - lastHeroThreatAt) /
                                Mathf.Max(.5f, heroThreatMemorySeconds));
            attackScore += urgency;
            advanceScore += urgency * .8f;
            specialScore += urgency * .6f;
        }

        // Hero is an obstacle, not the only objective. Long-distance chases stop
        // when a mine or ground gold can generate more meaningful progress.
        if (!heroThreatensThisUnit)
        {
            attackScore = 0f;
            retreatScore = 0f;
            specialScore = 0f;
            advanceScore *= .3f;
        }
        else
        {
            float danger = Mathf.Clamp01(1f - distance / Mathf.Max(1f, heroInterventionRadius));
            attackScore += danger * 22f * defensiveAggression;
            advanceScore += danger * 18f * defensiveAggression;
            specialScore += danger * 16f * defensiveAggression;
        }

        // Guards never desert their pillar for money.
        if (GuardPillar != null)
        {
            if (!HeroIsRelevantToGuardZone(distance))
            {
                attackScore = 0f;
                advanceScore = 0f;
                retreatScore = 0f;
                specialScore = 0f;
                guardScore = Mathf.Max(guardScore, 16f);
            }
        }

        GoldPickup candidateCoin = null;
        GoldMine candidateMine = null;
        float collectScore = 0f;
        float mineScore = 0f;

        if (canGatherGold && economicMotivation > 0f && GuardPillar == null)
        {
            float urgency = 1f;
            HeroDoomGameManager game = HeroDoomGameManager.Instance;
            if (game != null)
                urgency += .42f * Mathf.Clamp01(
                    (lowGoldThreshold - (float)System.Math.Min(game.Gold, lowGoldThreshold)) /
                    Mathf.Max(1f, lowGoldThreshold));

            foreach (GoldPickup coin in GoldPickup.Active)
            {
                if (coin == null || !coin.CanClaim(this)) continue;
                float d = FlatDistance(transform.position, coin.transform.position);
                if (d > economicSearchRadius) continue;
                float score = (72f + Mathf.Clamp((float)coin.goldValue, 0f, 25f) * .22f -
                              d * 1.65f) * economicMotivation * urgency;
                if (coin == targetGoldPickup) score += 13f;
                if (heroThreatensThisUnit) score -= 20f * defensiveAggression;
                if (score > collectScore)
                {
                    collectScore = score;
                    candidateCoin = coin;
                }
            }

            foreach (GoldMine mine in GoldMine.Active)
            {
                if (mine == null || !mine.CanWork(this)) continue;
                float d = FlatDistance(transform.position, mine.transform.position);
                if (d > economicSearchRadius) continue;
                float workers = Mathf.Max(1, mine.WorkerCount + (targetGoldMine == mine ? 0 : 1));
                float yieldRate = (float)(mine.goldPerWork / Mathf.Max(.4f, mine.workDuration));
                float score = (55f + Mathf.Clamp(yieldRate * 1.1f, 0f, 25f) -
                              d * 1.25f - workers * 2f) * economicMotivation * urgency;
                if (mine == targetGoldMine) score += 13f;
                if (heroThreatensThisUnit) score -= 17f * defensiveAggression;
                if (score > mineScore)
                {
                    mineScore = score;
                    candidateMine = mine;
                }
            }
        }

        if (heroThreatensThisUnit)
        {
            // Stop gambling on economy when a living Hero is about to interrupt.
            float emergencyDistance = Mathf.Max(4.5f, attackRange + 2f);
            if (distance < emergencyDistance || hitRecently)
            {
                collectScore *= .20f;
                mineScore *= .10f;
            }
            else
            {
                collectScore *= .75f;
                mineScore *= .60f;
            }
        }

        // At most two path checks per unit per decision, rather than one for
        // every pickup/deposit on every frame.
        if (candidateCoin != null && !HasEconomicPath(candidateCoin.transform.position))
        {
            candidateCoin = null;
            collectScore = 0f;
        }
        if (candidateMine != null && !HasEconomicPath(candidateMine.transform.position))
        {
            candidateMine = null;
            mineScore = 0f;
        }

        if (GuardPillar != null)
        {
            collectScore = 0f;
            mineScore = 0f;
        }

        attackScore += currentUtilityAction == MonsterUtilityAction.Attack ? currentActionInertia : 0f;
        advanceScore += currentUtilityAction == MonsterUtilityAction.Advance ? currentActionInertia : 0f;
        retreatScore += currentUtilityAction == MonsterUtilityAction.Retreat ? currentActionInertia : 0f;
        specialScore += currentUtilityAction == MonsterUtilityAction.MageSpecial ? currentActionInertia : 0f;
        guardScore += currentUtilityAction == MonsterUtilityAction.ReturnToGuard ? currentActionInertia : 0f;
        collectScore += currentUtilityAction == MonsterUtilityAction.CollectGold && candidateCoin != null
            ? currentActionInertia : 0f;
        mineScore += currentUtilityAction == MonsterUtilityAction.MineGold && candidateMine != null
            ? currentActionInertia : 0f;

        MonsterUtilityAction best = MonsterUtilityAction.Idle;
        float bestScore = idleScore;
        ConsiderUtilityCandidate(MonsterUtilityAction.Advance, advanceScore, ref best, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.Attack, attackScore, ref best, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.Retreat, retreatScore, ref best, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.MageSpecial, specialScore, ref best, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.ReturnToGuard, guardScore, ref best, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.CollectGold, collectScore, ref best, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.MineGold, mineScore, ref best, ref bestScore);

        bool invalid = !IsCurrentUtilityActionValid();
        bool commitmentExpired = Time.time >= actionCommittedUntil;
        bool significantGain = bestScore >= currentUtilityScore + actionSwitchThreshold;
        bool urgentDefense = heroThreatensThisUnit &&
                             best != MonsterUtilityAction.MineGold &&
                             best != MonsterUtilityAction.CollectGold &&
                             bestScore >= 82f;

        if (best != currentUtilityAction &&
            (invalid || urgentDefense || (commitmentExpired && significantGain)))
        {
            currentUtilityAction = best;
            currentUtilityScore = bestScore;
            actionCommittedUntil = Time.time + Mathf.Max(0f, minimumActionCommitment);
        }
        else
        {
            currentUtilityScore = ScoreAction(
                currentUtilityAction, advanceScore, attackScore, retreatScore,
                specialScore, guardScore, collectScore, mineScore, idleScore);
        }

        if (currentUtilityAction == MonsterUtilityAction.CollectGold)
        {
            if (targetGoldMine != null) ReleaseMine();
            if (candidateCoin != targetGoldPickup)
            {
                ReleasePickup();
                if (candidateCoin != null && candidateCoin.TryClaim(this))
                    targetGoldPickup = candidateCoin;
                else
                    nextDecisionAt = 0f;
            }
        }
        else if (currentUtilityAction == MonsterUtilityAction.MineGold)
        {
            ReleasePickup();
            if (candidateMine != targetGoldMine)
            {
                ReleaseMine();
                if (candidateMine != null && candidateMine.Reserve(this))
                {
                    targetGoldMine = candidateMine;
                    miningProgress = 0f;
                }
                else
                    nextDecisionAt = 0f;
            }
        }
        else
            ReleaseGoldTasks();
    }

    private bool HasEconomicPath(Vector3 point)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || economicPath == null) return false;
        return agent.CalculatePath(point, economicPath) &&
               economicPath.status == NavMeshPathStatus.PathComplete;
    }

    private float ScoreAction(
        MonsterUtilityAction action, float advance, float attack, float retreat,
        float special, float guard, float collect, float mine, float idle)
    {
        return action switch
        {
            MonsterUtilityAction.Advance => advance,
            MonsterUtilityAction.Attack => attack,
            MonsterUtilityAction.Retreat => retreat,
            MonsterUtilityAction.MageSpecial => special,
            MonsterUtilityAction.ReturnToGuard => guard,
            MonsterUtilityAction.CollectGold => collect,
            MonsterUtilityAction.MineGold => mine,
            _ => idle
        };
    }

    private static void ConsiderUtilityCandidate(
        MonsterUtilityAction candidate,
        float score,
        ref MonsterUtilityAction bestAction,
        ref float bestScore)
    {
        if (score <= bestScore) return;
        bestScore = score;
        bestAction = candidate;
    }

    private float UtilityScoreFor(
        MonsterUtilityAction action,
        float advance,
        float attack,
        float retreat,
        float special,
        float guard,
        float idle)
    {
        return action switch
        {
            MonsterUtilityAction.Advance => advance,
            MonsterUtilityAction.Attack => attack,
            MonsterUtilityAction.Retreat => retreat,
            MonsterUtilityAction.MageSpecial => special,
            MonsterUtilityAction.ReturnToGuard => guard,
            _ => idle
        };
    }

    private bool HeroIsRelevantToGuardZone(float distance)
    {
        if (heroTarget == null || !heroTarget.IsAlive) return false;
        if (GuardPillar == null) return true;
        float engagePadding = role == MonsterRole.Mage ? preferredRange + 1f : attackRange + 2f;
        return GuardPillar.IsInside(heroTarget.transform.position, attackRange + 1.5f) &&
               distance <= Mathf.Max(attackRange, engagePadding);
    }

    private float ScoreAttack(float distance)
    {
        if (heroTarget == null || !heroTarget.IsAlive ||
            !HeroIsRelevantToGuardZone(distance) || distance > attackRange) return 0f;

        float score = 58f + (1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, attackRange))) * 16f;
        float heroHp = heroTarget.CurrentHP / Mathf.Max(1f, heroTarget.MaxHP);
        float teamPressure = BattlefieldTactics.FocusPressure(heroTarget);

        switch (role)
        {
            case MonsterRole.Assassin:
                score += (1f - heroHp) * 26f + teamPressure * 18f * teamPressureWeight;
                if (teamPressure < .25f && heroHp > .55f) score -= 16f;
                if (HPPercent < assassinDisengageHp) score -= 38f;
                break;
            case MonsterRole.Tank:
                score += 12f + (1f - teamPressure) * 7f;
                break;
            case MonsterRole.Mage:
                score -= distance < retreatRange ? 22f : 6f;
                score += teamPressure * 8f;
                break;
            default:
                score += teamPressure * 5f;
                break;
        }

        score -= BattlefieldTactics.SpacingPenalty(this, transform.position, allySpacing);
        return score;
    }

    private float ScoreAdvance(float distance)
    {
        if (!HeroIsRelevantToGuardZone(distance)) return 0f;

        float desiredRange = role == MonsterRole.Mage
            ? Mathf.Min(preferredRange, attackRange)
            : Mathf.Max(.8f, attackRange);

        if (distance <= desiredRange) return 0f;

        float normalizedDistance = Mathf.Clamp01(distance / 18f);
        float teamPressure = BattlefieldTactics.FocusPressure(heroTarget);
        float score = 34f + normalizedDistance * 18f;
        int frontline = BattlefieldTactics.CountRoleNear(
            MonsterRole.Tank, heroTarget.transform.position, 7.5f);

        switch (role)
        {
            case MonsterRole.Assassin:
                // Wait for allied pressure before committing to risky dives.
                score += 4f + teamPressure * 18f * teamPressureWeight;
                if (frontline > 0) score += 8f;
                if (HPPercent < assassinDisengageHp) score -= 30f;
                break;
            case MonsterRole.Tank:
                // Tank initiates when the team has not established a front line.
                score += 14f + (1f - teamPressure) * 11f;
                break;
            case MonsterRole.Mage:
                score += 3f + teamPressure * 5f;
                if (frontline > 0) score += 5f;
                break;
            default:
                score += 5f;
                break;
        }

        Vector3 formation = BattlefieldTactics.FormationPoint(this, PredictedHeroPosition(.25f), desiredRange);
        score += Mathf.Clamp01(FlatDistance(transform.position, formation) / Mathf.Max(1f, desiredRange)) *
                 14f * formationWeight;
        score -= BattlefieldTactics.SpacingPenalty(this, transform.position, allySpacing);
        return Mathf.Max(0f, score);
    }

    private float ScoreRetreat(float distance)
    {
        if (Time.time < nextRetreatAt || !HeroIsRelevantToGuardZone(distance)) return 0f;

        float lowHealth = 1f - HPPercent;
        float teamPressure = BattlefieldTactics.FocusPressure(heroTarget);

        if (role == MonsterRole.Mage)
        {
            if (distance >= retreatRange && HPPercent > .42f) return 0f;
            float closeness = 1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, retreatRange));
            return 66f + closeness * 28f + lowHealth * 20f - teamPressure * 5f;
        }

        if (role == MonsterRole.Assassin)
        {
            bool badDive = HPPercent <= assassinDisengageHp;
            bool waitingForStrike = attackTimer > attackCooldown * .45f && distance < attackRange + 1.2f;
            if (!badDive && !waitingForStrike) return 0f;
            return 62f + lowHealth * 32f + (1f - teamPressure) * 12f;
        }

        return 0f;
    }

    private float ScoreMageSpecial(float distance)
    {
        if (role != MonsterRole.Mage || specialTimer > 0f) return 0f;
        if (!HeroIsRelevantToGuardZone(distance) || distance > attackRange + 1.5f) return 0f;

        float lowHealth = 1f - HPPercent;
        float proximity = 1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, attackRange + 1.5f));
        return 52f + lowHealth * 26f + proximity * 12f;
    }

    private float ScoreReturnToGuard()
    {
        if (GuardPillar == null) return 0f;

        float distanceFromCenter = FlatDistance(transform.position, GuardPillar.transform.position);
        float desiredRadius = Mathf.Max(0.8f, GuardPillar.captureRadius * 0.55f);
        if (distanceFromCenter <= desiredRadius)
            return 0f;

        float outside = Mathf.Clamp01((distanceFromCenter - desiredRadius) / Mathf.Max(1f, GuardPillar.captureRadius));
        return 62f + outside * 22f;
    }

    private bool IsCurrentUtilityActionValid()
    {
        float distance = heroTarget != null && heroTarget.IsAlive
            ? FlatDistance(transform.position, heroTarget.transform.position)
            : float.PositiveInfinity;
        return currentUtilityAction switch
        {
            MonsterUtilityAction.Advance => ScoreAdvance(distance) > 0f,
            MonsterUtilityAction.Attack => ScoreAttack(distance) > 0f,
            MonsterUtilityAction.Retreat => ScoreRetreat(distance) > 0f || retreating,
            MonsterUtilityAction.MageSpecial => ScoreMageSpecial(distance) > 0f,
            MonsterUtilityAction.ReturnToGuard => GuardPillar != null,
            MonsterUtilityAction.CollectGold => targetGoldPickup != null && targetGoldPickup.CanClaim(this),
            MonsterUtilityAction.MineGold => targetGoldMine != null && targetGoldMine.CanWork(this),
            _ => true
        };
    }

    private void ExecuteUtilityAction()
    {
        float distance = heroTarget != null && heroTarget.IsAlive
            ? FlatDistance(transform.position, heroTarget.transform.position)
            : float.PositiveInfinity;

        switch (currentUtilityAction)
        {
            case MonsterUtilityAction.Attack:
                if (heroTarget == null || !heroTarget.IsAlive) { StopMoving(); break; }
                StopMoving();
                Face(heroTarget.transform.position);
                if (attackTimer <= 0f) AttackHero(role == MonsterRole.Tank);
                break;

            case MonsterUtilityAction.Advance:
            {
                if (heroTarget == null || !heroTarget.IsAlive) { StopMoving(); break; }
                float formationRange = role == MonsterRole.Mage
                    ? Mathf.Clamp(preferredRange, .8f, Mathf.Max(.8f, attackRange - .25f))
                    : Mathf.Max(.8f, attackRange * .82f);
                Vector3 destination = TacticalApproachPoint(formationRange);
                // A formation point is already offset from the Hero. Stopping at
                // attackRange again would place this unit OUTSIDE attack range.
                MoveTo(destination, .2f);
                break;
            }

            case MonsterUtilityAction.Retreat:
                if (heroTarget != null && heroTarget.IsAlive) ExecuteRetreatAction(distance);
                else StopMoving();
                break;

            case MonsterUtilityAction.MageSpecial:
                if (heroTarget == null || !heroTarget.IsAlive) { StopMoving(); break; }
                StopMoving();
                Face(heroTarget.transform.position);
                CastMageSpecial();
                attackTimer = Mathf.Max(attackTimer, castStandTime);
                castEndsAt = Time.time + castStandTime;
                nextDecisionAt = 0f;
                break;

            case MonsterUtilityAction.ReturnToGuard:
                if (GuardPillar != null)
                {
                    if (FlatDistance(transform.position, GuardPillar.transform.position) >
                        Mathf.Max(.9f, GuardPillar.captureRadius * .55f))
                        MoveTo(GuardPillar.transform.position, .55f);
                    else StopMoving();
                }
                else StopMoving();
                break;

            case MonsterUtilityAction.CollectGold:
                ExecuteCollectGold();
                break;

            case MonsterUtilityAction.MineGold:
                ExecuteMineGold();
                break;

            default:
                StopMoving();
                break;
        }
    }

    private Vector3 TacticalApproachPoint(float range)
    {
        if (heroTarget == null) return transform.position;
        float lead = role == MonsterRole.Assassin ? .42f : role == MonsterRole.Tank ? .25f : .3f;
        Vector3 predicted = PredictedHeroPosition(lead);
        Vector3 destination = BattlefieldTactics.FormationPoint(this, predicted, range);
        if (dynamicFormationWeight > 0f)
        {
            Vector3 heading = heroVelocityEstimate;
            heading.y = 0f;
            if (heading.sqrMagnitude < .6f)
            {
                heading = heroTarget.transform.forward;
                heading.y = 0f;
            }
            if (heading.sqrMagnitude > .01f)
            {
                heading.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, heading);
                switch (role)
                {
                    case MonsterRole.Tank:
                        // Intercept from the front to screen allied ranged units.
                        destination = Vector3.Lerp(destination,
                            predicted + heading * range * .65f,
                            Mathf.Clamp01(dynamicFormationWeight));
                        break;
                    case MonsterRole.Assassin:
                        // Alternate left/right lanes; approach from the blind side.
                        float side = (GetInstanceID() & 1) == 0 ? 1f : -1f;
                        destination = Vector3.Lerp(destination,
                            predicted - heading * range * .65f + right * side * range * .75f,
                            Mathf.Clamp01(dynamicFormationWeight));
                        break;
                    case MonsterRole.Mage:
                        // Stay on the outside of the fight, avoiding the melee core.
                        Vector3 away = transform.position - predicted;
                        away.y = 0f;
                        if (away.sqrMagnitude < .01f) away = -heading;
                        destination = Vector3.Lerp(destination,
                            predicted + away.normalized * range,
                            Mathf.Clamp01(dynamicFormationWeight * .75f));
                        break;
                }
            }
        }

        if (GuardPillar != null) destination = ClampToGuardZone(destination);
        return destination;
    }

    private void ExecuteCollectGold()
    {
        if (targetGoldPickup == null || !targetGoldPickup.CanClaim(this))
        {
            ReleasePickup();
            nextDecisionAt = 0f;
            StopMoving();
            return;
        }

        float distance = FlatDistance(transform.position, targetGoldPickup.transform.position);
        if (distance <= Mathf.Max(.5f, targetGoldPickup.pickupRadius))
        {
            StopMoving();
            if (targetGoldPickup.TryCollect(this))
            {
                ReleasePickup();
                nextDecisionAt = 0f;
            }
            return;
        }

        MoveTo(targetGoldPickup.transform.position, .25f);
    }

    private void ExecuteMineGold()
    {
        if (heroTarget != null && heroTarget.IsAlive &&
            Time.time - lastHeroThreatAt <= heroThreatMemorySeconds &&
            FlatDistance(transform.position, heroTarget.transform.position) <= heroInterventionRadius + 5f)
        {
            ReleaseMine();
            nextDecisionAt = 0f;
            StopMoving();
            return;
        }
        if (targetGoldMine == null || !targetGoldMine.CanWork(this))
        {
            ReleaseMine();
            nextDecisionAt = 0f;
            StopMoving();
            return;
        }

        float distance = FlatDistance(transform.position, targetGoldMine.transform.position);
        if (distance > targetGoldMine.workRadius * .9f)
        {
            miningProgress = 0f;
            MoveTo(targetGoldMine.transform.position, Mathf.Max(.5f, targetGoldMine.workRadius * .55f));
            return;
        }

        StopMoving();
        Face(targetGoldMine.transform.position);
        miningProgress += Time.deltaTime;
        float interval = customWorkInterval > 0f
            ? customWorkInterval
            : targetGoldMine.workDuration;

        if (miningProgress >= Mathf.Max(.25f, interval))
        {
            miningProgress = 0f;
            Trigger("Attack");
            if (!targetGoldMine.Harvest(this))
            {
                nextDecisionAt = 0f;
                return;
            }
            CollectGoldAtFeet();
        }
    }

    private void CollectGoldAtFeet()
    {
        if (!canGatherGold) return;
        float radiusSqr = goldAutoPickupRadius * goldAutoPickupRadius;
        foreach (GoldPickup coin in GoldPickup.Active)
        {
            if (coin == null || !coin.CanCollect) continue;
            Vector3 delta = coin.transform.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSqr && coin.TryCollect(this))
            {
                if (targetGoldPickup == coin)
                {
                    ReleasePickup();
                    nextDecisionAt = 0f;
                }
                break;
            }
        }
    }

    private void ReleaseMine()
    {
        if (targetGoldMine != null) targetGoldMine.Release(this);
        targetGoldMine = null;
        miningProgress = 0f;
    }

    private void ReleasePickup()
    {
        if (targetGoldPickup != null) targetGoldPickup.Release(this);
        targetGoldPickup = null;
    }

    private void ReleaseGoldTasks()
    {
        ReleaseMine();
        ReleasePickup();
    }

    public void ClearGoldPickup(GoldPickup pickup)
    {
        if (targetGoldPickup == pickup)
        {
            targetGoldPickup = null;
            nextDecisionAt = 0f;
        }
    }

    public void ClearGoldMine(GoldMine mine)
    {
        if (targetGoldMine == mine)
        {
            targetGoldMine = null;
            miningProgress = 0f;
            nextDecisionAt = 0f;
        }
    }

    public void SetPlayerControlled(bool controlled)
    {
        IsPlayerControlled = controlled && IsAlive;
        if (IsPlayerControlled) ReleaseGoldTasks();
        hasPlayerMoveTarget = false;
        nextDecisionAt = 0f;
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
    }

    public void SetPlayerMoveTarget(Vector3 destination)
    {
        if (!IsPlayerControlled || !IsAlive) return;
        playerMoveTarget = destination;
        hasPlayerMoveTarget = true;
    }

    private void ExecuteRetreatAction(float distance)
    {
        if (role != MonsterRole.Mage && role != MonsterRole.Assassin)
        {
            StopMoving();
            return;
        }

        if (!retreating)
        {
            float roleCooldown = role == MonsterRole.Assassin ? retreatCooldown * .7f : retreatCooldown;
            nextRetreatAt = Time.time + retreatDuration + roleCooldown;

            if (TryChooseRetreatPoint(out retreatDestination))
            {
                retreating = true;
                retreatEndsAt = Time.time + (role == MonsterRole.Assassin ? retreatDuration * .75f : retreatDuration);
            }
            else
            {
                currentUtilityAction = MonsterUtilityAction.Attack;
                nextDecisionAt = 0f;
                return;
            }
        }

        if (Time.time < retreatEndsAt && FlatDistance(transform.position, retreatDestination) > 0.35f)
        {
            MoveTo(retreatDestination, 0.15f);
            return;
        }

        retreating = false;
        nextDecisionAt = 0f;
        StopMoving();
    }

    private void HandleRoleBehaviour()
    {
        float distance = FlatDistance(transform.position, heroTarget.transform.position);

        if (role == MonsterRole.Mage)
        {
            HandleMageBehaviour(distance);
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
                HandleMageBehaviour(heroDistance);
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

    private void HandleMageBehaviour(float distance)
    {
        // Commit to a short escape, then stand and fight even if the hero keeps chasing.
        if (Time.time < castEndsAt)
        {
            StopMoving();
            Face(heroTarget.transform.position);
            return;
        }
        if (retreating)
        {
            if (Time.time < retreatEndsAt && FlatDistance(transform.position, retreatDestination) > 0.35f)
            {
                MoveTo(retreatDestination, 0.15f);
                return;
            }
            retreating = false;
        }
        if (distance < retreatRange && Time.time >= nextRetreatAt)
        {
            nextRetreatAt = Time.time + retreatDuration + retreatCooldown;
            if (TryChooseRetreatPoint(out retreatDestination))
            {
                retreating = true;
                retreatEndsAt = Time.time + retreatDuration;
                MoveTo(retreatDestination, 0.15f);
                return;
            }
        }
        float desiredRange = Mathf.Min(preferredRange, attackRange);
        if (distance > desiredRange + rangeHysteresis) approaching = true;
        if (distance <= desiredRange) approaching = false;
        if (approaching)
        {
            float formationRange = role == MonsterRole.Mage ? Mathf.Min(preferredRange, attackRange) : Mathf.Max(.8f, attackRange * .82f);
                Vector3 destination = BattlefieldTactics.FormationPoint(this, PredictedHeroPosition(.32f), formationRange);
                if (GuardPillar != null) destination = ClampToGuardZone(destination);
            MoveTo(destination, Mathf.Max(0.2f, desiredRange - 0.25f));
            return;
        }
        StopMoving();
        Face(heroTarget.transform.position);
        if (distance > attackRange) return;
        if (specialTimer <= 0 && (HPPercent < 0.55f || distance <= preferredRange))
        {
            CastMageSpecial();
            attackTimer = Mathf.Max(attackTimer, castStandTime);
            castEndsAt = Time.time + castStandTime;
        }
        else if (attackTimer <= 0)
        {
            AttackHero(false);
            castEndsAt = Time.time + castStandTime;
        }
    }

    private bool TryChooseRetreatPoint(out Vector3 point)
    {
        Vector3 away = transform.position - PredictedHeroPosition(.2f);
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        away.Normalize();

        point = transform.position;
        float bestScore = float.MinValue;
        MonsterAI tankAnchor = role == MonsterRole.Mage
            ? BattlefieldTactics.ClosestAllyOfRole(this, MonsterRole.Tank)
            : null;

        for (int i = 0; i < 7; i++)
        {
            float angle = (i - 3) * 32f;
            Vector3 candidate = transform.position + Quaternion.Euler(0, angle, 0) * away * retreatDistance;
            if (GuardPillar != null) candidate = ClampToGuardZone(candidate);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.1f, agent.areaMask)) continue;
            if (GuardPillar != null && !GuardPillar.IsInside(hit.position, 0f)) continue;
            if (FlatDistance(transform.position, hit.position) < 0.6f) continue;

            retreatPath ??= new NavMeshPath();
            if (!agent.CalculatePath(hit.position, retreatPath) ||
                retreatPath.status != NavMeshPathStatus.PathComplete) continue;

            float heroDistance = FlatDistance(hit.position, PredictedHeroPosition(.25f));
            float score = heroDistance * 3f;
            score -= BattlefieldTactics.SpacingPenalty(this, hit.position, allySpacing) * 1.4f;

            if (role == MonsterRole.Mage && tankAnchor != null)
            {
                float anchorDistance = FlatDistance(hit.position, tankAnchor.transform.position);
                score += Mathf.Clamp01(1f - anchorDistance / 8f) * 12f * mageAnchorWeight;
            }

            if (role == MonsterRole.Assassin)
                score += Mathf.Abs(Vector3.Dot((hit.position - transform.position).normalized, transform.right)) * 3f;

            if (score > bestScore)
            {
                bestScore = score;
                point = hit.position;
            }
        }

        return bestScore > float.MinValue;
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

        CapturePillarManager pillars = CapturePillarManager.Instance;
        float damage = attackDamage * (pillars != null ? pillars.MonsterDamageMultiplier : 1f);
        float heroHp = heroTarget.CurrentHP / Mathf.Max(1f, heroTarget.MaxHP);

        if (role == MonsterRole.Assassin)
        {
            float teamPressure = BattlefieldTactics.FocusPressure(heroTarget);
            damage *= 1f + (1f - heroHp) * .18f + teamPressure * .08f;
        }

        Vector3 push = (heroTarget.transform.position - transform.position).normalized;

        if (role == MonsterRole.Mage)
        {
            GameAudioController.Instance?.PlayMagic();
            MagicProjectile.Spawn(
                transform.position + Vector3.up * 1.25f,
                heroTarget,
                this,
                damage,
                knockbackForce,
                mageProjectilePrefab);
        }
        else
        {
            GameAudioController.Instance?.PlayMeleeHit();
            heroTarget.TakeDamage(damage, this, push * knockbackForce, heavy);
        }
    }

    private void CastMageSpecial()
    {
        specialTimer = Mathf.Max(1f, specialCooldown);
        Trigger("Attack");

        if (HPPercent < 0.55f)
        {
            float heal = maxHP * 0.18f;
            heal = Mathf.Min(heal, maxHP - currentHP);
            currentHP += heal;
            DamagePopup.Spawn(damagePopupPrefab, transform.position + Vector3.up * 2, heal, DamagePopupKind.Healing);
        }

        if (heroTarget != null && heroTarget.IsAlive && FlatDistance(transform.position, heroTarget.transform.position) <= attackRange + 1.5f)
        {
            GameAudioController.Instance?.PlayMagic();
            MagicProjectile.SpawnLightning(lightningPrefab, transform.position + Vector3.up * 1.35f, heroTarget.transform.position + Vector3.up);
            Vector3 push = (heroTarget.transform.position - transform.position).normalized;
            heroTarget.TakeDamage(specialDamage, this, push * (knockbackForce + 0.8f), true);
        }
    }

    public float TakeDamage(float amount, bool heavyHit)
    {
        if (!IsAlive || amount <= 0f || (spawnProtection != null && spawnProtection.IsProtected)) return 0f;

        if (role == MonsterRole.Tank && Random.value < 0.22f)
            amount *= 0.45f;

        CapturePillarManager pillars = CapturePillarManager.Instance;
        float pillarProtection = pillars != null ? Mathf.Max(1f, pillars.MonsterHealthMultiplier) : 1f;
        float actual = Mathf.Min(currentHP, amount * (1f - armor) / pillarProtection);
        currentHP -= actual;
        if (actual > 0f)
        {
            lastHeroThreatAt = Time.time;
            nextDecisionAt = 0f;
        }
        if (actual > 0f)
            HeroSkillBurst.Spawn(hitEffectPrefab, transform.position + Vector3.up * 0.9f);

        DamagePopup.Spawn(
            damagePopupPrefab,
            transform.position + Vector3.up * 1.9f,
            actual,
            DamagePopupKind.DamageToMonster,
            heavyHit);

        if (currentHP <= 0f) Die(true);
        else Trigger(heavyHit ? "HitHeavy" : "HitLight");

        return actual;
    }

    public void ApplyKnockback(Vector3 velocity)
    {
        if (!IsAlive || (spawnProtection != null && spawnProtection.IsProtected)) return;
        knockbackMotor?.Push(velocity);
    }

    public void KillByEnvironment()
    {
        if (!IsAlive) return;
        currentHP = 0f;
        Die(false);
    }

    private void Die(bool dropExperience)
    {
        if (!IsAlive) return;
        IsAlive = false;
        spawnProtection?.End();
        if (knockbackMotor != null) knockbackMotor.enabled = false;
        ActiveMonsters.Remove(this);
        ReleaseGoldTasks();

        // Phát event NGAY khi HP về 0, trước animation corpse.
        // Hero sẽ bỏ focus trong cùng frame dù object vẫn còn tồn tại.
        Died?.Invoke(this);

        if (GuardPillar != null)
        {
            GuardPillar.UnregisterGuard(this);
            GuardPillar = null;
        }

        StopMoving();
        if (agent != null) agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        Trigger("Die");
        GameAudioController.Instance?.PlayMonsterDeath();
        if (dropExperience)
            ExperienceOrb.Spawn(experienceOrbPrefab, transform.position, GetXPValue());
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
        ReleaseGoldTasks();
        currentUtilityAction = MonsterUtilityAction.ReturnToGuard;
        nextDecisionAt = 0f;
    }

    public void ClearGuard(CapturePillar pillar)
    {
        if (GuardPillar == pillar) { GuardPillar = null; nextDecisionAt = 0f; }
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
        rb = GetComponent<Rigidbody>();
        agent = GetComponent<NavMeshAgent>();
        if (spawnProtection == null) spawnProtection = GetComponent<SpawnProtection>();
        if (knockbackMotor == null) knockbackMotor = GetComponent<CombatKnockback>();
        if (agent == null) Debug.LogError("Monster prefab needs an authored NavMeshAgent.", this);
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

    private void MoveTo(Vector3 destination, float stoppingDistance = -1)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = false;
        agent.speed = moveSpeed;
        agent.stoppingDistance = stoppingDistance >= 0f
            ? stoppingDistance : Mathf.Max(.2f, attackRange * .8f);

        // Recalculate at a controlled rate, including failed paths. This avoids
        // repeated per-frame pathfinding when a moving target is unreachable.
        if (Time.time >= nextPathUpdate &&
            (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid ||
             (destination - lastDestination).sqrMagnitude > .75f ||
             agent.remainingDistance <= agent.stoppingDistance + .2f))
        {
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, agent.areaMask))
            {
                agent.SetDestination(hit.position);
                lastDestination = destination;
            }
            nextPathUpdate = Time.time + .22f;
        }
        SetSpeed(agent.velocity.magnitude);
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




/// <summary>
/// Shared, allocation-free tactical information used by Hero and monster utility AI.
/// It turns individual actions into coordinated positioning: spacing, focus pressure and role formation.
/// </summary>
public static class BattlefieldTactics
{
    public static int CountAlliesNear(MonsterAI self, Vector3 point, float radius)
    {
        int count = 0;
        float radiusSqr = radius * radius;

        foreach (MonsterAI ally in MonsterAI.ActiveMonsters)
        {
            if (ally == null || !ally.IsAlive || ally == self) continue;
            Vector3 delta = ally.transform.position - point;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSqr) count++;
        }

        return count;
    }

    public static int CountRoleNear(MonsterRole role, Vector3 point, float radius)
    {
        int count = 0;
        float radiusSqr = radius * radius;

        foreach (MonsterAI ally in MonsterAI.ActiveMonsters)
        {
            if (ally == null || !ally.IsAlive || ally.role != role) continue;
            Vector3 delta = ally.transform.position - point;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSqr) count++;
        }

        return count;
    }

    public static float FocusPressure(HeroBotAI hero)
    {
        if (hero == null || !hero.IsAlive) return 0f;
        int nearby = CountAlliesNear(null, hero.transform.position, 4.5f);
        return Mathf.Clamp01(nearby / 4f);
    }

    public static MonsterAI ClosestAllyOfRole(MonsterAI self, MonsterRole role)
    {
        MonsterAI best = null;
        float bestSqr = float.MaxValue;

        foreach (MonsterAI ally in MonsterAI.ActiveMonsters)
        {
            if (ally == null || !ally.IsAlive || ally == self || ally.role != role) continue;
            float sqr = (ally.transform.position - self.transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = ally;
            }
        }

        return best;
    }

    public static Vector3 MonsterCentroid(Vector3 point, float radius)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        float radiusSqr = radius * radius;

        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
        {
            if (monster == null || !monster.IsAlive) continue;
            Vector3 delta = monster.transform.position - point;
            delta.y = 0f;
            if (delta.sqrMagnitude > radiusSqr) continue;
            sum += monster.transform.position;
            count++;
        }

        return count > 0 ? sum / count : point;
    }

    public static float NearestMonsterDistance(Vector3 point, float fallback)
    {
        float best = fallback;
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
        {
            if (monster == null || !monster.IsAlive) continue;
            Vector3 delta = monster.transform.position - point;
            delta.y = 0f;
            best = Mathf.Min(best, delta.magnitude);
        }
        return best;
    }

    public static Vector3 FormationPoint(MonsterAI self, HeroBotAI hero, float range)
    {
        return FormationPoint(
            self,
            hero != null ? hero.transform.position : self.transform.position,
            range);
    }

    public static Vector3 FormationPoint(MonsterAI self, Vector3 center, float range)
    {
        float baseAngle = self.role switch
        {
            MonsterRole.Tank => 0f,
            MonsterRole.Mage => 130f,
            MonsterRole.Assassin => -120f,
            _ => 180f
        };

        // Stable world-space slot: turning the unit no longer rotates the whole formation.
        float variation = Mathf.Repeat(Mathf.Abs(self.GetInstanceID()) * 47.31f, 70f) - 35f;
        Vector3 direction = Quaternion.Euler(0f, baseAngle + variation, 0f) * Vector3.forward;
        return center + direction.normalized * Mathf.Max(.35f, range);
    }

    public static float SpacingPenalty(MonsterAI self, Vector3 point, float desiredSpacing)
    {
        return CountAlliesNear(self, point, desiredSpacing) * 5f;
    }
}

/// <summary>Authored world-space indicator for a monster's tactical intent. Runtime changes text only.</summary>
public sealed class MonsterTacticalIndicator : MonoBehaviour
{
    public MonsterAI monster;
    public TMP_Text intentText;
    public SpriteRenderer bubble;
    public Vector3 worldOffset = new(0f, 2.9f, 0f);
    public float fadeSpeed = 8f;
    private Camera viewCamera;
    private string lastIntent;

    void Awake()
    {
        if (monster == null) monster = GetComponentInParent<MonsterAI>();
        viewCamera = Camera.main;
        SetIntent(monster != null ? monster.CurrentTacticalAction : MonsterUtilityAction.Idle);
    }

    void LateUpdate()
    {
        if (monster == null || !monster.IsAlive) { gameObject.SetActive(false); return; }
        SetIntent(monster.CurrentTacticalAction);
        transform.position = monster.transform.position + worldOffset;
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera != null) transform.rotation = Quaternion.LookRotation(viewCamera.transform.forward, viewCamera.transform.up);
    }

    public void SetIntent(MonsterUtilityAction action)
    {
        string intent = action switch
        {
            MonsterUtilityAction.Advance => "→ FLANK",
            MonsterUtilityAction.Attack => "! STRIKE",
            MonsterUtilityAction.Retreat => "← FALL BACK",
            MonsterUtilityAction.MageSpecial => "✦ CAST",
            MonsterUtilityAction.ReturnToGuard => "⌂ GUARD",
            MonsterUtilityAction.CollectGold => "$ COLLECT",
            MonsterUtilityAction.MineGold => "$ MINING",
            _ => "… THINK"
        };
        if (intent == lastIntent) return;
        lastIntent = intent;
        if (intentText != null) intentText.text = intent;
        if (bubble != null) bubble.color = action switch
        {
            MonsterUtilityAction.Retreat => new Color(1f,.54f,.18f,.95f),
            MonsterUtilityAction.CollectGold => new Color(1f,.8f,.25f,.95f),
            MonsterUtilityAction.MineGold => new Color(.98f,.73f,.12f,.95f),
            MonsterUtilityAction.MageSpecial => new Color(.32f,.8f,1f,.95f),
            MonsterUtilityAction.Attack => new Color(1f,.3f,.22f,.95f),
            _ => new Color(.9f,.92f,.78f,.92f)
        };
    }
}
