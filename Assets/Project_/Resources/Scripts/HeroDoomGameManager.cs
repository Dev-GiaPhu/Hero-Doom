using UnityEngine;

public class HeroDoomGameManager : MonoBehaviour
{
    public static HeroDoomGameManager Instance { get; private set; }

    [Header("Hero Lifecycle")]
    public GameObject heroPrefab;
    public float baseHeroHP = 500f;
    public float heroHPPerDeath = 75f;
    public float respawnDelay = 5f;
    public float respawnSearchRadius = 10f;
    [Min(0f)] public float respawnMinimumDistance = 4f;
    [Range(4, 64)] public int respawnRandomAttempts = 24;

    [Header("Economy")]
    public double startingGold = 50d;
    public double passiveGoldPerSecond = 2d;
    public double goldPerDamage = 0.5d;

    [Header("Scene References")]
    public InfiniteMapGenerator map;
    public PlayerSpawner playerSpawner;
    public CapturePillarManager pillarManager;

    public HeroBotAI CurrentHero { get; private set; }
    public int HeroDeaths { get; private set; }
    public double Gold { get; private set; }
    public bool IsRespawning => respawnTimer > 0f;
    public float RespawnRemaining => Mathf.Max(0f, respawnTimer);

    private readonly int[] persistentHeroUpgradeStacks = new int[12];
    private int persistentHeroLevel = 1;
    private float persistentHeroExperience;

    private float respawnTimer;
    private Vector3 lastHeroDeathPosition;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        Gold = startingGold;
    }

    void Start()
    {
        HeroBotAI hero = FindFirstObjectByType<HeroBotAI>();

        if (hero != null)
            RegisterHero(hero);
        else
            SpawnHero(Vector3.zero);
    }

    void Update()
    {
        Gold += passiveGoldPerSecond * GetGoldMultiplier() * Time.deltaTime;

        if (respawnTimer > 0f)
        {
            respawnTimer -= Time.deltaTime;

            if (respawnTimer <= 0f)
                SpawnHero(lastHeroDeathPosition);
        }
    }

    public void RegisterHero(HeroBotAI hero)
    {
        if (hero == null) return;

        CurrentHero = hero;

        float permanentMaxHP =
            baseHeroHP + heroHPPerDeath * HeroDeaths;

        hero.SetPermanentDifficulty(permanentMaxHP);
        hero.RestoreProgression(persistentHeroLevel, persistentHeroExperience, persistentHeroUpgradeStacks);
    }

    public void SaveHeroProgression(HeroBotAI hero)
    {
        if (hero == null) return;

        persistentHeroLevel = Mathf.Max(1, hero.Level);
        persistentHeroExperience = Mathf.Max(0f, hero.Experience);

        for (int i = 0; i < persistentHeroUpgradeStacks.Length; i++)
            persistentHeroUpgradeStacks[i] = hero.GetUpgradeStack((HeroUpgradeType)i);
    }

    public void NotifyHeroDied(HeroBotAI hero)
    {
        if (hero == null ||
            hero != CurrentHero ||
            respawnTimer > 0f)
            return;

        SaveHeroProgression(hero);
        lastHeroDeathPosition = hero.transform.position;
        CurrentHero = null;
        HeroDeaths++;
        respawnTimer = Mathf.Max(0.1f, respawnDelay);
    }

    private void SpawnHero(Vector3 near)
    {
        if (heroPrefab == null)
        {
            Debug.LogError("[HeroDoom] Hero Prefab chưa được gán trong GameManager Inspector.");
            return;
        }

        Vector3 spawn = near;

        if (!TryGetRandomRespawnPoint(near, out spawn) &&
            map != null &&
            !map.TryGetSafeGround(near, respawnSearchRadius, out spawn))
        {
            map.TryGetSafeGround(
                Vector3.zero,
                Mathf.Max(20f, respawnSearchRadius),
                out spawn);
        }

        GameObject go = Instantiate(heroPrefab, spawn, Quaternion.identity);
        go.name = "Hero";

        HeroBotAI ai = go.GetComponent<HeroBotAI>();

        if (ai == null)
        {
            Debug.LogError(
                "[HeroDoom] Hero prefab chưa có HeroBotAI. Gắn HeroBotAI trực tiếp lên prefab để chỉnh trong Inspector.");
            Destroy(go);
            return;
        }

        RegisterHero(ai);
    }

    private bool TryGetRandomRespawnPoint(Vector3 near, out Vector3 spawn)
    {
        spawn = near;
        if (map == null) return false;

        float minRadius = Mathf.Clamp(respawnMinimumDistance, 0f, respawnSearchRadius);
        float maxRadius = Mathf.Max(minRadius + 0.5f, respawnSearchRadius);

        for (int i = 0; i < Mathf.Max(4, respawnRandomAttempts); i++)
        {
            Vector2 dir = Random.insideUnitCircle;
            if (dir.sqrMagnitude < 0.01f) dir = Vector2.right;
            dir.Normalize();

            float distance = Random.Range(minRadius, maxRadius);
            Vector3 candidate = near + new Vector3(dir.x, 0f, dir.y) * distance;

            if (!map.IsInsideWorld(candidate) || map.IsWaterAtWorldPosition(candidate))
                continue;

            if (map.TryGetSafeGround(candidate, 2.5f, out Vector3 safe) &&
                Vector3.Distance(new Vector3(safe.x, 0f, safe.z), new Vector3(near.x, 0f, near.z)) >= minRadius * 0.75f)
            {
                spawn = safe;
                return true;
            }
        }

        return false;
    }

    public bool CanAfford(double amount)
    {
        return Gold + 0.0001d >= amount;
    }

    public bool SpendGold(double amount)
    {
        if (amount <= 0d) return true;
        if (!CanAfford(amount)) return false;

        Gold -= amount;
        return true;
    }

    public void AddDamageGold(float actualDamage)
    {
        if (actualDamage <= 0f) return;

        Gold +=
            actualDamage *
            goldPerDamage *
            GetGoldMultiplier();
    }

    public float GetGoldMultiplier()
    {
        return pillarManager != null
            ? pillarManager.GoldMultiplier
            : 1f;
    }

    public static string FormatNumber(double value)
    {
        string[] suffix =
        {
            "", "K", "M", "B", "T",
            "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc"
        };

        bool negative = value < 0d;
        double abs = System.Math.Abs(value);

        if (abs < 1000d)
        {
            string integerText = System.Math.Floor(abs).ToString("0");
            return negative ? "-" + integerText : integerText;
        }

        int tier = 0;

        while (abs >= 1000d && tier < suffix.Length - 1)
        {
            abs /= 1000d;
            tier++;
        }

        if (tier == suffix.Length - 1 && abs >= 1000d)
        {
            string scientific = abs.ToString("0.###E+0");
            return negative ? "-" + scientific : scientific;
        }

        double rounded =
            System.Math.Round(
                abs,
                1,
                System.MidpointRounding.AwayFromZero);

        string format =
            System.Math.Abs(rounded - System.Math.Round(rounded)) < 0.0001d
                ? "0"
                : "0.0";

        string result = rounded.ToString(format) + suffix[tier];
        return negative ? "-" + result : result;
    }
}
