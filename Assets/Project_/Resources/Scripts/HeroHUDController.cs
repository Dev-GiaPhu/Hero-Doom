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

    void Awake()
    {
        if (gameManager == null) gameManager = FindFirstObjectByType<HeroDoomGameManager>();
        if (progressionRoot == null || experienceFill == null || upgradeIconRoot == null)
            Debug.LogError("[HeroDoom] Hero Progression HUD is not authored. Run Hero Doom > Author Combat Assets and Upgrade Panel.", this);
    }

    void Update()
    {
        if (gameManager == null) gameManager = FindFirstObjectByType<HeroDoomGameManager>();
        if (gameManager == null) return;
        HeroBotAI hero = gameManager.CurrentHero;
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
        SetExperienceVisual(0f, "EXP --");
        RefreshUpgradeIcons(null);
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
            int stack = hero != null && hero.IsAlive ? hero.GetUpgradeStack(slot.type) : 0;
            slot.root.SetActive(stack > 0);
            if (stack <= 0) continue;
            HeroUpgradeDefinition definition = hero.GetUpgradeDefinition(slot.type);
            if (slot.icon != null) slot.icon.sprite = definition != null ? definition.icon : null;
            if (slot.count != null) slot.count.text = $"x{stack}";
        }
    }
}
