using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HeroHUDController : MonoBehaviour
{
    [Header("UI References")]
    public Slider healthSlider;
    public TMP_Text healthText;

    [Header("Hero Progression UI")]
    public RectTransform progressionRoot;
    public Image experienceFill;
    public TMP_Text experienceText;
    public RectTransform upgradeIconRoot;
    [Min(18f)] public float upgradeIconSize = 34f;
    [Min(0f)] public float upgradeIconSpacing = 5f;
    public Vector2 progressionOffset = new Vector2(0f, -8f);

    [Header("Data Source")]
    public HeroDoomGameManager gameManager;

    [Header("Display")]
    public bool showLevel = true;
    public string respawnPrefix = "Respawn";

    private readonly Dictionary<HeroUpgradeType, UpgradeIconView> upgradeViews = new();
    private HeroBotAI lastHero;

    private sealed class UpgradeIconView
    {
        public GameObject root;
        public Image icon;
        public TMP_Text count;
    }

    void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();

        EnsureProgressionUI();
    }

    void Update()
    {
        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();
            if (gameManager == null) return;
        }

        if (progressionRoot == null)
            EnsureProgressionUI();

        HeroBotAI hero = gameManager.CurrentHero;

        if (hero != null && hero.IsAlive)
        {
            if (hero != lastHero)
            {
                lastHero = hero;
                RefreshUpgradeIcons(true);
            }

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
            RefreshUpgradeIcons(false);
            return;
        }

        lastHero = null;

        if (healthSlider != null)
            healthSlider.value = 0f;

        if (healthText != null)
        {
            healthText.text = gameManager.IsRespawning
                ? $"{respawnPrefix} {gameManager.RespawnRemaining:0.0}s"
                : "Hero defeated";
        }

        SetExperienceVisual(0f, "EXP --");
        RefreshUpgradeIcons(true);
    }

    private void UpdateExperience(HeroBotAI hero)
    {
        float required = Mathf.Max(1f, hero.ExperienceToNext);
        float ratio = Mathf.Clamp01(hero.Experience / required);
        SetExperienceVisual(
            ratio,
            $"EXP  {Mathf.FloorToInt(hero.Experience)} / {Mathf.CeilToInt(required)}");
    }

    private void SetExperienceVisual(float ratio, string text)
    {
        if (experienceFill != null)
        {
            RectTransform fillRect = experienceFill.rectTransform;
            Vector2 max = fillRect.anchorMax;
            max.x = Mathf.Clamp01(ratio);
            fillRect.anchorMax = max;
        }

        if (experienceText != null)
            experienceText.text = text;
    }

    private void EnsureProgressionUI()
    {
        if (progressionRoot != null) return;
        if (healthSlider == null) return;

        RectTransform healthRect = healthSlider.transform as RectTransform;
        RectTransform parent = healthRect != null ? healthRect.parent as RectTransform : null;
        if (parent == null) return;

        Transform existing = parent.Find("Hero Progression HUD");
        if (existing != null)
        {
            progressionRoot = existing as RectTransform;
            CacheGeneratedReferences();
            return;
        }

        GameObject root = new GameObject("Hero Progression HUD", typeof(RectTransform));
        progressionRoot = root.GetComponent<RectTransform>();
        progressionRoot.SetParent(parent, false);

        if (healthRect != null)
        {
            progressionRoot.anchorMin = healthRect.anchorMin;
            progressionRoot.anchorMax = healthRect.anchorMax;
            progressionRoot.pivot = healthRect.pivot;
            progressionRoot.sizeDelta = new Vector2(healthRect.rect.width, 82f);
            progressionRoot.anchoredPosition =
                healthRect.anchoredPosition +
                new Vector2(progressionOffset.x, -Mathf.Max(18f, healthRect.rect.height) + progressionOffset.y);
        }
        else
        {
            progressionRoot.anchorMin = new Vector2(0f, 1f);
            progressionRoot.anchorMax = new Vector2(0f, 1f);
            progressionRoot.pivot = new Vector2(0f, 1f);
            progressionRoot.sizeDelta = new Vector2(280f, 82f);
        }

        CreateExperienceBar(progressionRoot);
        CreateUpgradeRow(progressionRoot);
    }

    private void CreateExperienceBar(RectTransform parent)
    {
        GameObject bgGo = new GameObject("Experience Bar", typeof(RectTransform), typeof(Image));
        RectTransform bg = bgGo.GetComponent<RectTransform>();
        bg.SetParent(parent, false);
        bg.anchorMin = new Vector2(0f, 1f);
        bg.anchorMax = new Vector2(1f, 1f);
        bg.pivot = new Vector2(0.5f, 1f);
        bg.offsetMin = new Vector2(0f, -20f);
        bg.offsetMax = new Vector2(0f, -4f);

        Image background = bgGo.GetComponent<Image>();
        background.color = new Color(0.035f, 0.055f, 0.075f, 0.92f);
        background.raycastTarget = false;

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        RectTransform fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.SetParent(bg, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);

        experienceFill = fillGo.GetComponent<Image>();
        experienceFill.color = new Color(0.28f, 0.72f, 1f, 1f);
        experienceFill.raycastTarget = false;

        GameObject textGo = new GameObject("Experience Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.SetParent(bg, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        experienceText = textGo.GetComponent<TextMeshProUGUI>();
        experienceText.font = TMP_Settings.defaultFontAsset;
        experienceText.text = "EXP  0 / 10";
        experienceText.fontSize = 12f;
        experienceText.fontStyle = FontStyles.Bold;
        experienceText.alignment = TextAlignmentOptions.Center;
        experienceText.color = Color.white;
        experienceText.raycastTarget = false;
        experienceText.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void CreateUpgradeRow(RectTransform parent)
    {
        GameObject rowGo = new GameObject(
            "Upgrade Icons",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup));

        upgradeIconRoot = rowGo.GetComponent<RectTransform>();
        upgradeIconRoot.SetParent(parent, false);
        upgradeIconRoot.anchorMin = new Vector2(0f, 1f);
        upgradeIconRoot.anchorMax = new Vector2(1f, 1f);
        upgradeIconRoot.pivot = new Vector2(0.5f, 1f);
        upgradeIconRoot.offsetMin = new Vector2(0f, -72f);
        upgradeIconRoot.offsetMax = new Vector2(0f, -28f);

        HorizontalLayoutGroup layout = rowGo.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = upgradeIconSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 0, 2, 2);
    }

    private void CacheGeneratedReferences()
    {
        if (progressionRoot == null) return;

        Transform exp = progressionRoot.Find("Experience Bar");
        if (exp != null)
        {
            Transform fill = exp.Find("Fill");
            if (fill != null) experienceFill = fill.GetComponent<Image>();

            Transform label = exp.Find("Experience Text");
            if (label != null) experienceText = label.GetComponent<TMP_Text>();
        }

        Transform upgrades = progressionRoot.Find("Upgrade Icons");
        if (upgrades != null) upgradeIconRoot = upgrades as RectTransform;
    }

    private void RefreshUpgradeIcons(bool forceRebuild)
    {
        HeroBotAI hero = gameManager != null ? gameManager.CurrentHero : null;

        if (forceRebuild)
        {
            foreach (UpgradeIconView view in upgradeViews.Values)
                if (view?.root != null) Destroy(view.root);

            upgradeViews.Clear();

            if (upgradeIconRoot != null)
            {
                for (int i = upgradeIconRoot.childCount - 1; i >= 0; i--)
                    Destroy(upgradeIconRoot.GetChild(i).gameObject);
            }
        }

        if (hero == null || !hero.IsAlive || upgradeIconRoot == null)
            return;

        for (int i = 0; i < hero.UpgradeCount; i++)
        {
            HeroUpgradeType type = (HeroUpgradeType)i;
            int stack = hero.GetUpgradeStack(type);

            if (stack <= 0)
            {
                if (upgradeViews.TryGetValue(type, out UpgradeIconView hidden) && hidden.root != null)
                    hidden.root.SetActive(false);
                continue;
            }

            if (!upgradeViews.TryGetValue(type, out UpgradeIconView view) || view.root == null)
            {
                view = CreateUpgradeIcon(type, hero.GetUpgradeDefinition(type));
                upgradeViews[type] = view;
            }

            view.root.SetActive(true);
            if (view.count != null)
                view.count.text = stack > 1 ? $"x{stack}" : "x1";
        }
    }

    private UpgradeIconView CreateUpgradeIcon(HeroUpgradeType type, HeroUpgradeDefinition definition)
    {
        GameObject root = new GameObject(
            type + " Upgrade",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement));

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.SetParent(upgradeIconRoot, false);
        rt.sizeDelta = new Vector2(upgradeIconSize, upgradeIconSize);

        LayoutElement element = root.GetComponent<LayoutElement>();
        element.preferredWidth = upgradeIconSize;
        element.preferredHeight = upgradeIconSize;
        element.minWidth = upgradeIconSize;
        element.minHeight = upgradeIconSize;

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.04f, 0.07f, 0.08f, 0.9f);
        background.raycastTarget = false;

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform iconRect = iconGo.GetComponent<RectTransform>();
        iconRect.SetParent(rt, false);
        iconRect.anchorMin = new Vector2(0.12f, 0.12f);
        iconRect.anchorMax = new Vector2(0.88f, 0.88f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;

        Image icon = iconGo.GetComponent<Image>();
        icon.sprite = definition != null ? definition.icon : null;
        icon.preserveAspect = true;
        icon.color = Color.white;
        icon.raycastTarget = false;

        GameObject countGo = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform countRect = countGo.GetComponent<RectTransform>();
        countRect.SetParent(rt, false);
        countRect.anchorMin = new Vector2(0.35f, 0f);
        countRect.anchorMax = new Vector2(1f, 0.5f);
        countRect.offsetMin = Vector2.zero;
        countRect.offsetMax = new Vector2(-2f, 2f);

        TMP_Text count = countGo.GetComponent<TextMeshProUGUI>();
        count.font = TMP_Settings.defaultFontAsset;
        count.fontSize = 11f;
        count.fontStyle = FontStyles.Bold;
        count.alignment = TextAlignmentOptions.BottomRight;
        count.color = Color.white;
        count.raycastTarget = false;
        count.textWrappingMode = TextWrappingModes.NoWrap;

        return new UpgradeIconView
        {
            root = root,
            icon = icon,
            count = count
        };
    }
}
