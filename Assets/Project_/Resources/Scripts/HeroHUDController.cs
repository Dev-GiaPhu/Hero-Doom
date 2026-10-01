using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HeroHUDController : MonoBehaviour
{
    [Header("UI References")]
    public Slider healthSlider;
    public TMP_Text healthText;

    [Header("Authored Hero Progression UI")]
    public RectTransform progressionRoot;
    public Image experienceFill;
    public TMP_Text experienceText;
    public GameObject[] upgradeSlots = new GameObject[12];
    public Image[] upgradeIcons = new Image[12];
    public TMP_Text[] upgradeCounts = new TMP_Text[12];

    [Header("Data Source")]
    public HeroDoomGameManager gameManager;

    [Header("Display")]
    public bool showLevel = true;
    public string respawnPrefix = "Respawn";

    void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();
    }

    void Update()
    {
        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();
            if (gameManager == null) return;
        }

        HeroBotAI hero = gameManager.CurrentHero;

        if (hero != null && hero.IsAlive)
        {
            float ratio = hero.MaxHP <= 0f
                ? 0f
                : hero.CurrentHP / hero.MaxHP;

            if (healthSlider != null)
                healthSlider.value = Mathf.Clamp01(ratio);

            if (healthText != null)
            {
                string level = showLevel
                    ? $"  Lv.{hero.Level}"
                    : string.Empty;

                healthText.text =
                    $"{Mathf.CeilToInt(hero.CurrentHP)}/" +
                    $"{Mathf.CeilToInt(hero.MaxHP)}" +
                    level;
            }

            UpdateExperience(hero);
            UpdateUpgradeSlots(hero);
            return;
        }

        if (healthSlider != null)
            healthSlider.value = 0f;

        if (healthText != null)
        {
            healthText.text = gameManager.IsRespawning
                ? $"{respawnPrefix} {gameManager.RespawnRemaining:0.0}s"
                : "Hero defeated";
        }

        if (experienceFill != null)
            experienceFill.fillAmount = 0f;

        if (experienceText != null)
            experienceText.text = "EXP --";

        HideAllUpgradeSlots();
    }

    private void UpdateExperience(HeroBotAI hero)
    {
        float required = Mathf.Max(1f, hero.ExperienceToNext);
        float ratio = Mathf.Clamp01(hero.Experience / required);

        if (experienceFill != null)
            experienceFill.fillAmount = ratio;

        if (experienceText != null)
            experienceText.text =
                $"EXP  {Mathf.FloorToInt(hero.Experience)} / {Mathf.CeilToInt(required)}";
    }

    private void UpdateUpgradeSlots(HeroBotAI hero)
    {
        int count = Mathf.Min(
            hero.UpgradeCount,
            upgradeSlots != null ? upgradeSlots.Length : 0);

        for (int i = 0; i < count; i++)
        {
            HeroUpgradeType type = (HeroUpgradeType)i;
            int stack = hero.GetUpgradeStack(type);
            bool visible = stack > 0;

            if (upgradeSlots[i] != null)
                upgradeSlots[i].SetActive(visible);

            if (!visible) continue;

            HeroUpgradeDefinition definition = hero.GetUpgradeDefinition(type);

            if (upgradeIcons != null &&
                i < upgradeIcons.Length &&
                upgradeIcons[i] != null)
            {
                upgradeIcons[i].sprite = definition != null ? definition.icon : null;
            }

            if (upgradeCounts != null &&
                i < upgradeCounts.Length &&
                upgradeCounts[i] != null)
            {
                upgradeCounts[i].text = $"x{stack}";
            }
        }

        for (int i = count; upgradeSlots != null && i < upgradeSlots.Length; i++)
            if (upgradeSlots[i] != null)
                upgradeSlots[i].SetActive(false);
    }

    private void HideAllUpgradeSlots()
    {
        if (upgradeSlots == null) return;

        for (int i = 0; i < upgradeSlots.Length; i++)
            if (upgradeSlots[i] != null)
                upgradeSlots[i].SetActive(false);
    }
}
