using TMPro;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class HeroUpgradeSlotView
{
    public HeroUpgradeType type;
    public GameObject root;
    public Image icon;
    public TMP_Text count;
}

public class HeroHUDController : MonoBehaviour
{
    [Header("UI References")]
    public Slider healthSlider;
    public TMP_Text healthText;
    [Header("Authored progression UI")]
    public RectTransform progressionRoot;
    public Image experienceFill;
    public TMP_Text experienceText;
    public RectTransform upgradeIconRoot;
    public HeroUpgradeSlotView[] upgradeSlots = new HeroUpgradeSlotView[16];
    [Header("Data Source")]
    public HeroDoomGameManager gameManager;
    [Header("Display")]
    public bool showLevel = true;
    public string respawnPrefix = "Respawn";
    public HeroUpgradeTooltip upgradeTooltip;
    private HeroBotAI lastHero;

    void Awake()
    {
        if (gameManager == null) gameManager = FindFirstObjectByType<HeroDoomGameManager>();
        if (upgradeTooltip == null && upgradeSlots != null)
        {
            // The tooltip is a separate Canvas sibling in SampleScene, not a
            // child of the HUD. Reuse the references already authored on slots.
            foreach (HeroUpgradeSlotView slot in upgradeSlots)
            {
                if (slot == null || slot.root == null) continue;
                HeroUpgradeHover hover = slot.root.GetComponent<HeroUpgradeHover>();
                if (hover != null && hover.tooltip != null)
                {
                    upgradeTooltip = hover.tooltip;
                    break;
                }
            }
        }
        if (upgradeTooltip == null)
            upgradeTooltip = GetComponentInChildren<HeroUpgradeTooltip>(true);
        if (progressionRoot == null || experienceFill == null || upgradeIconRoot == null)
            Debug.LogError("[HeroDoom] Hero Progression HUD is not authored. Run Hero Doom > Author Combat Assets and Upgrade Panel.", this);
    }

    void Update()
    {
        if (gameManager == null) gameManager = FindFirstObjectByType<HeroDoomGameManager>();
        if (gameManager == null) return;
        HeroBotAI hero = gameManager.CurrentHero;
        if (hero != lastHero)
        {
            upgradeTooltip?.Hide();
            lastHero = hero;
        }
        if (hero != null && hero.IsAlive)
        {
            if (healthSlider != null) healthSlider.value = Mathf.Clamp01(hero.MaxHP <= 0f ? 0f : hero.CurrentHP / hero.MaxHP);
            if (healthText != null) healthText.text = $"{Mathf.CeilToInt(hero.CurrentHP)}/{Mathf.CeilToInt(hero.MaxHP)}" + (showLevel ? $"  Lv.{hero.Level}" : string.Empty);
            float required = Mathf.Max(1f, hero.ExperienceToNext);
            SetExperienceVisual(Mathf.Clamp01(hero.Experience / required), $"EXP  {Mathf.FloorToInt(hero.Experience)} / {Mathf.CeilToInt(required)}");
            RefreshUpgradeIcons(hero);
            return;
        }
        if (healthSlider != null) healthSlider.value = 0f;
        if (healthText != null) healthText.text = gameManager.IsRespawning ? $"{respawnPrefix} {gameManager.RespawnRemaining:0.0}s" : "Hero defeated";
        float savedRequired = Mathf.Max(1f, gameManager.SavedHeroExperienceToNext);
        SetExperienceVisual(
            Mathf.Clamp01(gameManager.SavedHeroExperience / savedRequired),
            $"EXP  {Mathf.FloorToInt(gameManager.SavedHeroExperience)} / {Mathf.CeilToInt(savedRequired)}   Lv.{gameManager.SavedHeroLevel}");
        // Keep already-earned upgrade icons through death / respawn.
        RefreshUpgradeIcons(null);
    }

    private void OnDisable()
    {
        upgradeTooltip?.Hide();
        lastHero = null;
    }

    private void SetExperienceVisual(float ratio, string text)
    {
        if (experienceFill != null)
            experienceFill.fillAmount = Mathf.Clamp01(ratio);
        if (experienceText != null) experienceText.text = text;
    }

    private void RefreshUpgradeIcons(HeroBotAI hero)
    {
        if (upgradeSlots == null) return;
        foreach (HeroUpgradeSlotView slot in upgradeSlots)
        {
            if (slot == null || slot.root == null) continue;
            int stack = hero != null && hero.IsAlive
                ? hero.GetUpgradeStack(slot.type)
                : gameManager != null ? gameManager.GetSavedHeroUpgradeStack(slot.type) : 0;
            slot.root.SetActive(stack > 0);
            if (stack <= 0) continue;
            HeroUpgradeDefinition definition = hero != null && hero.IsAlive
                ? hero.GetUpgradeDefinition(slot.type)
                : gameManager != null ? gameManager.GetHeroUpgradeDefinition(slot.type) : null;
            if (slot.icon != null && definition != null) slot.icon.sprite = definition.icon;
            if (slot.count != null) slot.count.text = $"x{stack}";
        }
    }
}
