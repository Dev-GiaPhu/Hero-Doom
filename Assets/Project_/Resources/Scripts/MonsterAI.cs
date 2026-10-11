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
    ReturnToGuard
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

    void Awake()
    {
        retreatPath = new NavMeshPath();
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
            heroTarget = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.CurrentHero : FindFirstObjectByType<HeroBotAI>();

        if (heroTarget == null || !heroTarget.IsAlive)
        {
            StopMoving();
            return;
        }

        if (!TryPlaceOnNavMesh()) return;
        UpdateHeroPrediction();

        if (IsPlayerControlled)
        {
            float heroDistance = FlatDistance(transform.position, heroTarget.transform.position);
            if (heroDistance <= attackRange && attackTimer <= 0f)
            {
                StopMoving();
                Face(heroTarget.transform.position);
                AttackHero(role == MonsterRole.Tank);
            }
            else if (hasPlayerMoveTarget && FlatDistance(transform.position, playerMoveTarget) > 0.55f)
                MoveTo(playerMoveTarget, 0.35f);
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
            nextDecisionAt = Time.time + Mathf.Max(0.05f, decisionInterval);
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
        float distance = FlatDistance(transform.position, heroTarget.transform.position);

        float attackScore = ScoreAttack(distance);
        float advanceScore = ScoreAdvance(distance);
        float retreatScore = ScoreRetreat(distance);
        float specialScore = ScoreMageSpecial(distance);
        float guardScore = ScoreReturnToGuard();
        float idleScore = 1f;

        attackScore += currentUtilityAction == MonsterUtilityAction.Attack ? currentActionInertia : 0f;
        advanceScore += currentUtilityAction == MonsterUtilityAction.Advance ? currentActionInertia : 0f;
        retreatScore += currentUtilityAction == MonsterUtilityAction.Retreat ? currentActionInertia : 0f;
        specialScore += currentUtilityAction == MonsterUtilityAction.MageSpecial ? currentActionInertia : 0f;
        guardScore += currentUtilityAction == MonsterUtilityAction.ReturnToGuard ? currentActionInertia : 0f;
        idleScore += currentUtilityAction == MonsterUtilityAction.Idle ? currentActionInertia : 0f;

        MonsterUtilityAction bestAction = MonsterUtilityAction.Idle;
        float bestScore = idleScore;
        ConsiderUtilityCandidate(MonsterUtilityAction.Advance, advanceScore, ref bestAction, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.Attack, attackScore, ref bestAction, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.Retreat, retreatScore, ref bestAction, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.MageSpecial, specialScore, ref bestAction, ref bestScore);
        ConsiderUtilityCandidate(MonsterUtilityAction.ReturnToGuard, guardScore, ref bestAction, ref bestScore);

        bool currentInvalid = !IsCurrentUtilityActionValid();
        bool commitmentExpired = Time.time >= actionCommittedUntil;
        bool clearlyBetter = bestScore >= currentUtilityScore + actionSwitchThreshold;
        bool emergency = bestScore >= 90f;

        if (bestAction != currentUtilityAction &&
            (currentInvalid || emergency || (commitmentExpired && clearlyBetter)))
        {
            currentUtilityAction = bestAction;
            currentUtilityScore = bestScore;
            actionCommittedUntil = Time.time + Mathf.Max(0f, minimumActionCommitment);
        }
        else
        {
            currentUtilityScore = UtilityScoreFor(
                currentUtilityAction,
                advanceScore,
                attackScore,
                retreatScore,
                specialScore,
                guardScore,
                idleScore);
        }
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
        if (GuardPillar == null) return true;
        float engagePadding = role == MonsterRole.Mage ? preferredRange + 1f : attackRange + 2f;
        return GuardPillar.IsInside(heroTarget.transform.position, attackRange + 1.5f) &&
               distance <= Mathf.Max(attackRange, engagePadding);
    }

    private float ScoreAttack(float distance)
    {
        if (!HeroIsRelevantToGuardZone(distance) || distance > attackRange) return 0f;

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

        switch (role)
        {
            case MonsterRole.Assassin:
                score += 9f + teamPressure * 18f * teamPressureWeight;
                if (HPPercent < assassinDisengageHp) score -= 30f;
                break;
            case MonsterRole.Tank:
                score += 14f + (1f - teamPressure) * 8f;
                break;
            case MonsterRole.Mage:
                score += 6f + teamPressure * 5f;
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
        float heroDistance = FlatDistance(transform.position, heroTarget.transform.position);

        if (distanceFromCenter <= desiredRadius)
            return 0f;

        float outside = Mathf.Clamp01((distanceFromCenter - desiredRadius) / Mathf.Max(1f, GuardPillar.captureRadius));
        return 62f + outside * 22f;
    }

    private bool IsCurrentUtilityActionValid()
    {
        if (heroTarget == null || !heroTarget.IsAlive) return false;

        float distance = FlatDistance(transform.position, heroTarget.transform.position);
        return currentUtilityAction switch
        {
            MonsterUtilityAction.Advance => ScoreAdvance(distance) > 0f,
            MonsterUtilityAction.Attack => ScoreAttack(distance) > 0f,
            MonsterUtilityAction.Retreat => ScoreRetreat(distance) > 0f || retreating,
            MonsterUtilityAction.MageSpecial => ScoreMageSpecial(distance) > 0f,
            MonsterUtilityAction.ReturnToGuard => ScoreReturnToGuard() > 0f,
            _ => true
        };
    }

    private void ExecuteUtilityAction()
    {
        float distance = FlatDistance(transform.position, heroTarget.transform.position);

        switch (currentUtilityAction)
        {
            case MonsterUtilityAction.Attack:
                StopMoving();
                Face(heroTarget.transform.position);
                if (attackTimer <= 0f) AttackHero(role == MonsterRole.Tank);
                break;

            case MonsterUtilityAction.Advance:
            {
                float formationRange = role == MonsterRole.Mage ? Mathf.Min(preferredRange, attackRange) : Mathf.Max(.8f, attackRange * .82f);
                Vector3 destination = BattlefieldTactics.FormationPoint(this, PredictedHeroPosition(.32f), formationRange);
                if (GuardPillar != null) destination = ClampToGuardZone(destination);
                float stoppingDistance = role == MonsterRole.Mage
                    ? Mathf.Max(0.2f, Mathf.Min(preferredRange, attackRange) - 0.25f)
                    : Mathf.Max(0.2f, attackRange * 0.8f);
                MoveTo(destination, stoppingDistance);
                break;
            }

            case MonsterUtilityAction.Retreat:
                ExecuteRetreatAction(distance);
                break;

            case MonsterUtilityAction.MageSpecial:
                StopMoving();
                Face(heroTarget.transform.position);
                CastMageSpecial();
                attackTimer = Mathf.Max(attackTimer, castStandTime);
                castEndsAt = Time.time + castStandTime;
                nextDecisionAt = 0f;
                break;

            case MonsterUtilityAction.ReturnToGuard:
                if (GuardPillar != null) MoveTo(GuardPillar.transform.position, 0.35f);
                else StopMoving();
                break;

            default:
                StopMoving();
                break;
        }
    }

    public void SetPlayerControlled(bool controlled)
    {
        IsPlayerControlled = controlled && IsAlive;
        hasPlayerMoveTarget = false;
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
        agent.stoppingDistance = stoppingDistance >= 0 ? stoppingDistance : Mathf.Max(0.2f, attackRange * 0.8f);
        if (!agent.hasPath || Time.time >= nextPathUpdate || (destination - lastDestination).sqrMagnitude > 1)
        {
            agent.SetDestination(destination);
            lastDestination = destination;
            nextPathUpdate = Time.time + 0.2f;
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
            _ => "… THINK"
        };
        if (intent == lastIntent) return;
        lastIntent = intent;
        if (intentText != null) intentText.text = intent;
        if (bubble != null) bubble.color = action switch
        {
            MonsterUtilityAction.Retreat => new Color(1f,.54f,.18f,.95f),
            MonsterUtilityAction.MageSpecial => new Color(.32f,.8f,1f,.95f),
            MonsterUtilityAction.Attack => new Color(1f,.3f,.22f,.95f),
            _ => new Color(.9f,.92f,.78f,.92f)
        };
    }
}
