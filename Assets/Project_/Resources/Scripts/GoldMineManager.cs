using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawns authored mine and pickup prefabs only on reachable safe land near active
/// gameplay. Mines have finite workers and yield actual world pickups.
/// </summary>
public sealed class GoldMineManager : MonoBehaviour
{
    public static GoldMineManager Instance { get; private set; }

    [Header("Authored Prefabs")]
    public GoldMine goldMinePrefab;
    public GoldPickup goldPickupPrefab;
    public InfiniteMapGenerator map;

    [Header("World Mining Economy")]
    [Range(1, 16)] public int targetMineCount = 7;
    [Header("Global Mining Pressure")]
    [Tooltip("Maximum monsters reserved across ALL mines; prevents parallel mine snowball.")]
    [Range(1, 16)] public int maxConcurrentMiners = 4;
    [Tooltip("Maximum miners on one mine, even if its prefab allows more.")]
    [Range(1, 4)] public int maxWorkersPerMine = 2;
    [Min(4f)] public float mineSpawnMinDistance = 10f;
    [Min(8f)] public float mineSpawnMaxDistance = 36f;
    [Min(4f)] public float mineSpacing = 11f;
    [Min(1f)] public float waterClearance = 2.5f;
    [Min(5f)] public float retireDistance = 68f;
    [Min(.2f)] public float refreshInterval = 2f;

    [Header("Loose World Coins")]
    [Range(0, 100)] public int targetLooseCoins = 16;
    [Range(1, 200)] public int maxWorldCoins = 80;
    [Min(1f)] public float looseGoldValue = 10f;
    [Min(1f)] public float looseCoinLifetime = 60f;
    [Min(.5f)] public float looseCoinSpawnInterval = 3.5f;
    [Range(1, 12)] public int looseCoinsPerInterval = 3;
    [Min(.5f)] public float looseCoinSpacing = 2f;
    [Min(1f)] public float minedCoinLifetime = 45f;
    [Range(1, 24)] public int placementAttemptsPerRefresh = 14;

    private readonly List<GoldMine> spawnedMines = new();
    private readonly List<GoldPickup> looseCoins = new();
    private readonly List<GoldMine> expiredMines = new();
    private float nextRefresh;
    private float nextLooseSpawnAt;
    private Vector3 lastHeroAnchor;
    private bool hasHeroAnchor;
    private NavMeshPath pathCheck;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        pathCheck = new NavMeshPath();
        if (map == null) map = FindFirstObjectByType<InfiniteMapGenerator>();
        if (goldMinePrefab == null || goldPickupPrefab == null)
            Debug.LogError("[Gold Economy] Assign authored Gold Mine and Gold Pickup prefabs in Inspector.", this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + Mathf.Max(.2f, refreshInterval);
        RefreshWorld();
    }

    public bool HasAvailableMinerSlot()
    {
        int reserved = 0;
        foreach (GoldMine mine in GoldMine.Active)
        {
            if (mine == null) continue;
            reserved += mine.WorkerCount;
            if (reserved >= Mathf.Max(1, maxConcurrentMiners)) return false;
        }
        return true;
    }

    public Vector3 EconomyAnchor => Anchor;

    private Vector3 Anchor
    {
        get
        {
            HeroBotAI hero = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.CurrentHero : null;
            if (hero != null && hero.IsAlive)
            {
                lastHeroAnchor = hero.transform.position;
                hasHeroAnchor = true;
                return lastHeroAnchor;
            }

            // Keep the economy stable across the Hero's respawn timer.
            if (hasHeroAnchor) return lastHeroAnchor;

            // No living Hero: the game keeps farming where monsters are active.
            foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
                if (monster != null && monster.IsAlive) return monster.transform.position;

            if (map != null && map.renderCamera != null)
            {
                Ray ray = map.renderCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
                Plane ground = new Plane(Vector3.up, Vector3.zero);
                if (ground.Raycast(ray, out float distance)) return ray.GetPoint(distance);
            }
            return Vector3.zero;
        }
    }

    private bool Safe(Vector3 point)
    {
        return map != null &&
               map.IsInsideWorld(point) &&
               map.IsSafeDistanceFromWater(point, waterClearance);
    }

    private bool TryPlace(Vector3 anchor, out Vector3 position)
    {
        position = Vector3.zero;
        if (map == null) return false;

        Vector2 offset = Random.insideUnitCircle.normalized *
            Random.Range(mineSpawnMinDistance, Mathf.Max(mineSpawnMinDistance + 1f, mineSpawnMaxDistance));
        Vector3 desired = anchor + new Vector3(offset.x, 0f, offset.y);
        if (!Safe(desired)) return false;
        if (!map.TryGetSafeGround(desired, map.maxChunkSize * 1.5f, out Vector3 ground)) return false;
        if (!Safe(ground)) return false;
        if (!NavMesh.SamplePosition(ground, out NavMeshHit nav, 1.8f, NavMesh.AllAreas)) return false;
        if (!Safe(nav.position)) return false;

        foreach (GoldMine mine in spawnedMines)
        {
            if (mine == null) continue;
            Vector3 delta = mine.transform.position - nav.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < mineSpacing * mineSpacing) return false;
        }

        // Require a complete route from at least one living monster or the Hero.
        bool accessible = false;
        foreach (MonsterAI unit in MonsterAI.ActiveMonsters)
        {
            if (unit == null || !unit.IsAlive) continue;
            if (!NavMesh.SamplePosition(unit.transform.position, out NavMeshHit origin, 1.2f, NavMesh.AllAreas)) continue;
            if (NavMesh.CalculatePath(origin.position, nav.position, NavMesh.AllAreas, pathCheck) &&
                pathCheck.status == NavMeshPathStatus.PathComplete)
            {
                accessible = true;
                break;
            }
        }

        if (!accessible)
        {
            if (NavMesh.SamplePosition(anchor, out NavMeshHit anchorNav, 2.5f, NavMesh.AllAreas))
                accessible = NavMesh.CalculatePath(anchorNav.position, nav.position, NavMesh.AllAreas, pathCheck) &&
                             pathCheck.status == NavMeshPathStatus.PathComplete;
        }

        if (!accessible) return false;
        position = nav.position;
        return true;
    }

    private void RefreshWorld()
    {
        if (map == null) map = FindFirstObjectByType<InfiniteMapGenerator>();
        if (map == null) return;
        Vector3 anchor = Anchor;

        expiredMines.Clear();
        foreach (GoldMine mine in spawnedMines)
        {
            if (mine == null) { expiredMines.Add(mine); continue; }
            Vector3 delta = mine.transform.position - anchor;
            delta.y = 0f;
            if (delta.sqrMagnitude > retireDistance * retireDistance)
            {
                expiredMines.Add(mine);
                Destroy(mine.gameObject);
            }
        }
        foreach (GoldMine mine in expiredMines) spawnedMines.Remove(mine);
        expiredMines.Clear();
        looseCoins.RemoveAll(coin => coin == null || !coin.CanCollect);

        if (goldMinePrefab != null)
        {
            for (int i = 0; i < placementAttemptsPerRefresh && spawnedMines.Count < targetMineCount; i++)
            {
                if (!TryPlace(anchor, out Vector3 pos)) continue;
                GoldMine mine = Instantiate(goldMinePrefab, pos, Quaternion.identity, transform);
                mine.name = "Gold Mine " + (spawnedMines.Count + 1);
                spawnedMines.Add(mine);
            }
        }

        if (goldPickupPrefab == null || Time.time < nextLooseSpawnAt) return;
        nextLooseSpawnAt = Time.time + Mathf.Max(.5f, looseCoinSpawnInterval);

        int createdCoins = 0;
        for (int i = 0; i < placementAttemptsPerRefresh &&
             createdCoins < looseCoinsPerInterval &&
             looseCoins.Count < targetLooseCoins &&
             GoldPickup.Active.Count < maxWorldCoins; i++)
        {
            if (!TryPlaceLooseCoin(anchor, out Vector3 pos)) continue;
            GoldPickup coin = Instantiate(goldPickupPrefab, pos, Quaternion.identity, transform);
            coin.Configure(looseGoldValue * Random.Range(.7f, 1.3f), looseCoinLifetime);
            coin.name = "World Gold";
            looseCoins.Add(coin);
            createdCoins++;
        }
    }

    private bool TryPlaceLooseCoin(Vector3 anchor, out Vector3 position)
    {
        position = Vector3.zero;
        if (map == null) return false;
        Vector2 v = Random.insideUnitCircle.normalized *
                    Random.Range(6f, Mathf.Max(8f, mineSpawnMaxDistance));
        Vector3 desired = anchor + new Vector3(v.x, 0f, v.y);
        if (!Safe(desired) || !map.TryGetSafeGround(desired, map.maxChunkSize, out Vector3 land))
            return false;
        if (!Safe(land) || !NavMesh.SamplePosition(land, out NavMeshHit nav, 1.5f, NavMesh.AllAreas))
            return false;
        if (!Safe(nav.position)) return false;
        foreach (GoldPickup nearby in looseCoins)
        {
            // Include coins still in their brief spawn animation when testing spacing.
            if (nearby == null || nearby.IsCollected) continue;
            Vector3 delta = nearby.transform.position - nav.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < looseCoinSpacing * looseCoinSpacing)
                return false;
        }
        position = nav.position;
        return true;
    }

    public bool SpawnMinedGold(MonsterAI miner, GoldMine mine, double value)
    {
        if (miner == null || mine == null || goldPickupPrefab == null ||
            GoldPickup.Active.Count >= maxWorldCoins)
            return false;

        Vector3 origin = Safe(miner.transform.position)
            ? miner.transform.position
            : mine.transform.position;
        // Drop close to the worker, but use the mine's validated safe ground
        // when the worker is temporarily standing near a dangerous shoreline.
        Vector2 offset = Random.insideUnitCircle * .7f;
        Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);
        Vector3 spawnPosition = Safe(candidate) ? candidate : mine.transform.position;
        if (!Safe(spawnPosition)) return false;

        GoldPickup coin = Instantiate(goldPickupPrefab, spawnPosition, Quaternion.identity, transform);
        coin.Configure(value, minedCoinLifetime);
        coin.name = "Mined Gold";
        return true;
    }

    public void ResetForRebirth()
    {
        foreach (GoldMine mine in spawnedMines) if (mine != null) Destroy(mine.gameObject);
        foreach (GoldPickup coin in GoldPickup.Active) if (coin != null) Destroy(coin.gameObject);
        spawnedMines.Clear();
        looseCoins.Clear();
        nextRefresh = Time.time + 1f;
        nextLooseSpawnAt = Time.time + 1f;
    }
}
