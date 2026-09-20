using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HeroDoomGameManager : MonoBehaviour
{
    public static HeroDoomGameManager Instance { get; private set; }

    [Header("Hero Lifecycle")]
    public GameObject heroPrefab;
    public float baseHeroHP = 500f;
    public float heroHPPerDeath = 75f;
    public float respawnDelay = 5f;
    public float respawnSearchRadius = 10f;

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

    private Slider heroSlider;
    private TMP_Text heroHPText;
    private TMP_Text goldText;
    private float respawnTimer;
    private Vector3 lastHeroDeathPosition;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        Gold = startingGold;

        if (map == null) map = FindFirstObjectByType<InfiniteMapGenerator>();
        if (playerSpawner == null) playerSpawner = FindFirstObjectByType<PlayerSpawner>();
        if (pillarManager == null) pillarManager = FindFirstObjectByType<CapturePillarManager>();
        if (heroPrefab == null) heroPrefab = Resources.Load<GameObject>("Prefabs/Charaters/Knight");

        GameObject hpRoot = GameObject.Find("HeroHPBar");
        if (hpRoot != null)
        {
            heroSlider = hpRoot.GetComponent<Slider>();
            heroHPText = hpRoot.GetComponentInChildren<TMP_Text>(true);
        }

        GameObject goldRoot = GameObject.Find("Souls");
        if (goldRoot != null) goldText = goldRoot.GetComponentInChildren<TMP_Text>(true);
    }

    void Start()
    {
        HeroBotAI hero = FindFirstObjectByType<HeroBotAI>();
        if (hero != null) RegisterHero(hero);
        else SpawnHero(Vector3.zero);
    }

    void Update()
    {
        Gold += passiveGoldPerSecond * GetGoldMultiplier() * Time.deltaTime;

        if (respawnTimer > 0f)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f) SpawnHero(lastHeroDeathPosition);
        }

        UpdateUI();
    }

    public void RegisterHero(HeroBotAI hero)
    {
        if (hero == null) return;
        CurrentHero = hero;
        float permanentMaxHP = baseHeroHP + heroHPPerDeath * HeroDeaths;
        hero.SetPermanentDifficulty(permanentMaxHP);
    }

    public void NotifyHeroDied(HeroBotAI hero)
    {
        if (hero == null || hero != CurrentHero || respawnTimer > 0f) return;
        lastHeroDeathPosition = hero.transform.position;
        CurrentHero = null;
        HeroDeaths++;
        respawnTimer = Mathf.Max(0.1f, respawnDelay);
    }

    private void SpawnHero(Vector3 near)
    {
        if (heroPrefab == null)
        {
            Debug.LogError("[HeroDoom] Không tìm thấy Knight hero prefab.");
            return;
        }

        Vector3 spawn = near;
        if (map != null && !map.TryGetSafeGround(near, respawnSearchRadius, out spawn))
            map.TryGetSafeGround(Vector3.zero, Mathf.Max(20f, respawnSearchRadius), out spawn);

        GameObject go = Instantiate(heroPrefab, spawn, Quaternion.identity);
        go.name = "Hero";
        HeroBotAI ai = go.GetComponent<HeroBotAI>();
        if (ai == null) ai = go.AddComponent<HeroBotAI>();
        RegisterHero(ai);
    }

    public bool CanAfford(double amount) => Gold + 0.0001d >= amount;

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
        Gold += actualDamage * goldPerDamage * GetGoldMultiplier();
    }

    public float GetGoldMultiplier()
    {
        return pillarManager != null ? pillarManager.GoldMultiplier : 1f;
    }

    private void UpdateUI()
    {
        if (goldText != null)
            goldText.text = FormatNumber(Gold);

        if (CurrentHero != null && CurrentHero.IsAlive)
        {
            float ratio = CurrentHero.MaxHP <= 0f ? 0f : CurrentHero.CurrentHP / CurrentHero.MaxHP;
            if (heroSlider != null) heroSlider.value = Mathf.Clamp01(ratio);
            if (heroHPText != null)
                heroHPText.text = $"{Mathf.CeilToInt(CurrentHero.CurrentHP)}/{Mathf.CeilToInt(CurrentHero.MaxHP)}  Lv.{CurrentHero.Level}";
        }
        else
        {
            if (heroSlider != null) heroSlider.value = 0f;
            if (heroHPText != null)
                heroHPText.text = respawnTimer > 0f ? $"Respawn {respawnTimer:0.0}s" : "Hero defeated";
        }
    }

    public static string FormatNumber(double value)
    {
        string[] suffix = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };
        double abs = System.Math.Abs(value);
        int tier = 0;
        while (abs >= 1000d && tier < suffix.Length - 1)
        {
            value /= 1000d;
            abs /= 1000d;
            tier++;
        }

        if (tier == suffix.Length - 1 && abs >= 1000d)
            return value.ToString("0.###E+0");

        string format = abs >= 100d ? "0" : abs >= 10d ? "0.0" : "0.00";
        return value.ToString(format) + suffix[tier];
    }
}
