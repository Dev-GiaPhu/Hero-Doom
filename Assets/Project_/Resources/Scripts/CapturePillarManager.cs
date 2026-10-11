using System.Collections.Generic;
using UnityEngine;

public class CapturePillarManager : MonoBehaviour
{
    public static CapturePillarManager Instance { get; private set; }

    [Header("Pillars")]
    [Min(1)] public int pillarCount = 3;
    public float captureRadius = 4f;
    public float goldBonusPerCapturedPillar = 0.12f;
    public float relocateInterval = 120f;
    public float edgePadding = 12f;
    public InfiniteMapGenerator map;
    [Header("Faction bonuses per owned pillar")]
    [Range(0f, 1f)] public float heroDamageBonusPerPillar = 0.08f;
    [Range(0f, 1f)] public float monsterDamageBonusPerPillar = 0.08f;
    [Range(0f, 0.5f)] public float heroHealthBonusPerPillar = 0.06f;
    [Range(0f, 0.5f)] public float monsterHealthBonusPerPillar = 0.06f;

    private readonly List<CapturePillar> pillars = new();
    private float relocateTimer;
    private GameObject pillarVisualPrefab;
    private float occupancyTimer;
    private float spawnRetryTimer;
    private bool pillarPrefabErrorLogged;
    public float HeroDamageMultiplier => 1f + CountOwned(PillarFaction.Hero) * heroDamageBonusPerPillar;
    public float MonsterDamageMultiplier => 1f + CountOwned(PillarFaction.Summoner) * monsterDamageBonusPerPillar;
    public float HeroHealthMultiplier => 1f + CountOwned(PillarFaction.Hero) * heroHealthBonusPerPillar;
    public float MonsterHealthMultiplier => 1f + CountOwned(PillarFaction.Summoner) * monsterHealthBonusPerPillar;

    public float GoldMultiplier
    {
        get
        {
            int captured = 0;
            foreach (CapturePillar p in pillars)
                if (p != null && p.faction == PillarFaction.Summoner) captured++;
            return 1f + captured * goldBonusPerCapturedPillar;
        }
    }

    void Awake()
    {
        Instance = this;
        if (map == null) map = FindFirstObjectByType<InfiniteMapGenerator>();
        // The project already ships the pillar as Resources/Prefabs/Environment/pillar.
        // Keep the manager pointed at that authored asset so the component the user
        // sees in the Project window is the exact prefab used at runtime.
        pillarVisualPrefab = Resources.Load<GameObject>("Prefabs/Environment/pillar");
    }

    void Start()
    {
        SpawnAll();
        relocateTimer = relocateInterval;
    }

    void Update()
    {
        occupancyTimer -= Time.deltaTime;
        if (occupancyTimer <= 0f)
        {
            occupancyTimer = 0.2f;
            RefreshPillars();
        }
        relocateTimer -= Time.deltaTime;
        if (relocateTimer <= 0f)
        {
            SpawnAll();
            relocateTimer = Mathf.Max(10f, relocateInterval);
        }
        if (pillars.Count < pillarCount && Time.time >= spawnRetryTimer)
        {
            spawnRetryTimer = Time.time + 2f;
            SpawnMissingPillars();
        }
    }

    private void SpawnAll()
    {
        foreach (CapturePillar p in pillars)
        {
            if (p == null) continue;
            foreach (MonsterAI guard in new List<MonsterAI>(p.guards))
                if (guard != null) guard.ClearGuard(p);
            Destroy(p.gameObject);
        }
        pillars.Clear();

        SpawnMissingPillars();
    }

    private void SpawnMissingPillars()
    {
        if (map == null) map = FindFirstObjectByType<InfiniteMapGenerator>();
        if (map == null) return;
        if (pillarVisualPrefab == null)
        {
            if(!pillarPrefabErrorLogged) Debug.LogError("Missing Resources/Prefabs/Environment/pillar.prefab.", this);
            pillarPrefabErrorLogged=true;
            return;
        }

        int attempts = Mathf.Max(8, pillarCount * 8);
        while (pillars.Count < pillarCount && attempts-- > 0)
        {
            if (!map.TryGetRandomSafeGround(edgePadding, out Vector3 pos)) continue;
            GameObject go = Instantiate(pillarVisualPrefab, pos, Quaternion.identity);

            go.name = $"EnergyPillar_{pillars.Count + 1}";
            CapturePillar pillar = go.GetComponent<CapturePillar>();
            if (pillar == null)
            {
                Debug.LogError("The authored pillar prefab needs CapturePillar component.", go);
                Destroy(go);
                pillarVisualPrefab = null;
                pillarPrefabErrorLogged=true;
                return;
            }
            pillar.captureRadius = captureRadius;
            pillar.goldBonus = goldBonusPerCapturedPillar;
            SphereCollider trigger = go.GetComponent<SphereCollider>();
            if (trigger != null) trigger.radius = captureRadius;

            pillars.Add(pillar);
        }
    }

    private int CountOwned(PillarFaction faction)
    {
        int count = 0;
        foreach (CapturePillar pillar in pillars) if (pillar != null && pillar.faction == faction) count++;
        return count;
    }

    private void RefreshPillars()
    {
        HeroBotAI hero = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.CurrentHero : null;
        var monsters = new List<MonsterAI>(MonsterAI.ActiveMonsters);
        foreach (CapturePillar pillar in pillars)
        {
            if (pillar == null) continue;
            bool heroInside = hero != null && hero.IsAlive && pillar.IsInside(hero.transform.position);
            bool monsterInside = false;
            foreach (MonsterAI monster in monsters)
                if (monster != null && monster.IsAlive && pillar.IsInside(monster.transform.position)) { monsterInside = true; break; }
            pillar.RefreshOccupancy(heroInside, monsterInside);
            pillar.TryFire(hero, monsters);
        }
    }

    public CapturePillar FindClosestCaptured(Vector3 position)
    {
        CapturePillar best = null;
        float bestSqr = float.MaxValue;
        foreach (CapturePillar p in pillars)
        {
            if (p == null || !p.IsCaptured) continue;
            float sqr = (p.transform.position - position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = p; }
        }
        return best;
    }

    public void TryAssignGuard(MonsterAI monster, Vector3 position)
    {
        if (monster == null) return;
        foreach (CapturePillar p in pillars)
        {
            if (p != null && p.IsInside(position))
            {
                p.RegisterGuard(monster);
                return;
            }
        }
    }

    public void ResetPillarControl()
    {
        foreach (CapturePillar pillar in pillars)
            if (pillar != null) pillar.RefreshOccupancy(false, false, true);
    }
}
