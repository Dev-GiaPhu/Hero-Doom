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

    [Header("Combat Feel")]
    public float rotationSpeed = 12f;
    public float knockbackForce = 0.7f;
    public float corpseHoldTime = 2.5f;
    public float shrinkDuration = 1.25f;

    public bool IsAlive { get; private set; } = true;
    public float HPPercent => maxHP <= 0f ? 0f : currentHP / maxHP;
    public CapturePillar GuardPillar { get; private set; }
    public bool IsPlayerControlled { get; private set; }
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
        TryPlaceOnNavMesh();
        Trigger("SpawnGround");
    }

    public void ApplySummonerUpgrade(float healthMultiplier, float damageMultiplier)
    {
        float hpRatio = currentHP / Mathf.Max(1, maxHP);
        maxHP = maxHP / appliedHealthMultiplier * healthMultiplier;
        currentHP = maxHP * hpRatio;
        attackDamage = attackDamage / appliedDamageMultiplier * damageMultiplier;
        specialDamage = specialDamage / appliedDamageMultiplier * damageMultiplier;
        appliedHealthMultiplier = healthMultiplier;
        appliedDamageMultiplier = damageMultiplier;
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

        if (role == MonsterRole.Assassin) score += (1f - heroHp) * 20f;
        if (role == MonsterRole.Tank) score += 7f;
        if (role == MonsterRole.Mage) score -= 7f;

        return score;
    }

    private float ScoreAdvance(float distance)
    {
        if (!HeroIsRelevantToGuardZone(distance)) return 0f;

        float desiredRange = role == MonsterRole.Mage
            ? Mathf.Min(preferredRange, attackRange)
            : attackRange;

        if (distance <= desiredRange) return 0f;

        float normalizedDistance = Mathf.Clamp01(distance / 18f);
        float score = 34f + normalizedDistance * 18f;

        if (role == MonsterRole.Assassin) score += 12f;
        if (role == MonsterRole.Tank) score += 4f;
        if (role == MonsterRole.Mage) score += 5f;

        return score;
    }

    private float ScoreRetreat(float distance)
    {
        if (role != MonsterRole.Mage || Time.time < nextRetreatAt) return 0f;
        if (!HeroIsRelevantToGuardZone(distance) || distance >= retreatRange) return 0f;

        float closeness = 1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, retreatRange));
        float lowHealth = 1f - HPPercent;
        return 68f + closeness * 24f + lowHealth * 12f;
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
                Vector3 destination = heroTarget.transform.position;
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
        if (role != MonsterRole.Mage)
        {
            StopMoving();
            return;
        }

        if (!retreating)
        {
            nextRetreatAt = Time.time + retreatDuration + retreatCooldown;
            if (TryChooseRetreatPoint(out retreatDestination))
            {
                retreating = true;
                retreatEndsAt = Time.time + retreatDuration;
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
            Vector3 destination = heroTarget.transform.position;
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
        Vector3 away = transform.position - heroTarget.transform.position;
        away.y = 0;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        away.Normalize();
        point = transform.position;
        float bestScore = float.MinValue;
        for (int i = 0; i < 5; i++)
        {
            float angle = i == 0 ? 0 : (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 45;
            Vector3 candidate = transform.position + Quaternion.Euler(0, angle, 0) * away * retreatDistance;
            if (GuardPillar != null) candidate = ClampToGuardZone(candidate);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.8f, agent.areaMask)) continue;
            if (GuardPillar != null && !GuardPillar.IsInside(hit.position, 0)) continue;
            if (FlatDistance(transform.position, hit.position) < 0.6f) continue;
            if (retreatPath == null) retreatPath = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, retreatPath) || retreatPath.status != NavMeshPathStatus.PathComplete) continue;
            float score = FlatDistance(hit.position, heroTarget.transform.position);
            foreach (MonsterAI ally in ActiveMonsters)
                if (ally != null && ally != this && ally.IsAlive)
                    score -= Mathf.Max(0, 1.5f - FlatDistance(hit.position, ally.transform.position)) * 2;
            if (score > bestScore) { bestScore = score; point = hit.position; }
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
        if (role == MonsterRole.Assassin && heroTarget.CurrentHP / Mathf.Max(1f, heroTarget.MaxHP) < 0.35f)
            damage *= 1.2f;

        Vector3 push = (heroTarget.transform.position - transform.position).normalized;
        if (role == MonsterRole.Mage)
            MagicProjectile.Spawn(transform.position + Vector3.up * 1.25f, heroTarget, this, damage, knockbackForce, mageProjectilePrefab);
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
            heal = Mathf.Min(heal, maxHP - currentHP);
            currentHP += heal;
            DamagePopup.Spawn(damagePopupPrefab, transform.position + Vector3.up * 2, heal, DamagePopupKind.Healing);
        }

        if (heroTarget != null && heroTarget.IsAlive && FlatDistance(transform.position, heroTarget.transform.position) <= attackRange + 1.5f)
        {
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
