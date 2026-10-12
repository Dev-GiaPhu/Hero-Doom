using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>Shows authored Hero upgrade details in an Editor-created HUD panel.</summary>
public class HeroUpgradeTooltip : MonoBehaviour
{
    public RectTransform tooltipRoot;
    public Image icon;
    public TMP_Text title;
    public TMP_Text description;
    public TMP_Text rankDetails;
    private HeroDoomGameManager manager;
    private HeroUpgradeType shownType;
    private bool showing;

    private void Awake()
    {
        if (manager == null) manager = FindFirstObjectByType<HeroDoomGameManager>();
    }

    // PointerExit is not fired when an upgrade icon is disabled or destroyed.
    // Also close stale details when the Hero instance or selected stack changes.
    private void Update()
    {
        if (!showing || tooltipRoot == null || !tooltipRoot.gameObject.activeSelf) return;
        if (manager == null) manager = FindFirstObjectByType<HeroDoomGameManager>();
        int stack = manager != null && manager.CurrentHero != null && manager.CurrentHero.IsAlive
            ? manager.CurrentHero.GetUpgradeStack(shownType)
            : manager != null ? manager.GetSavedHeroUpgradeStack(shownType) : 0;
        if (stack <= 0) Hide();
    }

    public void Show(HeroUpgradeType type, PointerEventData pointer)
    {
        if (tooltipRoot == null || pointer == null) return;
        if (manager == null) manager = FindFirstObjectByType<HeroDoomGameManager>();
        HeroBotAI hero = manager != null ? manager.CurrentHero : null;
        HeroUpgradeDefinition definition = manager != null ? manager.GetHeroUpgradeDefinition(type) : null;
        if (definition == null) { Hide(); return; }

        if (icon != null) icon.sprite = definition.icon;
        if (title != null) title.text = $"{(definition.category == HeroUpgradeCategory.Skill ? "SKILL" : "STAT")}  |  {definition.displayName}";
        if (description != null) description.text = definition.description;
        int rank = hero != null && hero.IsAlive
            ? hero.GetUpgradeStack(type)
            : manager != null ? manager.GetSavedHeroUpgradeStack(type) : 0;
        if (rankDetails != null)
        {
            string current = EffectAtRank(type, Mathf.Max(1, rank));
            string next = rank >= 5 ? "MAX STACK" : EffectAtRank(type, rank + 1);
            rankDetails.text = rank > 0
                ? $"CURRENT  x{rank}: {current}\nNEXT  x{rank + 1}: {next}"
                : $"NOT YET CHOSEN\nNEXT  x1: {next}";
        }
        shownType = type;
        showing = true;
        tooltipRoot.gameObject.SetActive(true);
        PositionAtPointer(pointer.position);
    }

    public void PositionAtPointer(Vector2 screenPosition)
    {
        if (tooltipRoot == null) return;
        Canvas canvas = tooltipRoot.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        RectTransform canvasRect = canvas.transform as RectTransform;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, uiCamera, out Vector2 local)) return;
        Vector2 size = tooltipRoot.rect.size;
        Vector2 offset = new Vector2(18f, -18f);
        float scale = canvas.scaleFactor;
        if (local.x + offset.x + size.x > canvasRect.rect.xMax) offset.x = -size.x - 18f;
        if (local.y + offset.y - size.y < canvasRect.rect.yMin) offset.y = size.y + 18f;
        tooltipRoot.anchoredPosition = local + offset / Mathf.Max(.01f, scale);
    }

    public void Hide()
    {
        showing = false;
        if (tooltipRoot != null && tooltipRoot.gameObject.activeSelf)
            tooltipRoot.gameObject.SetActive(false);
    }

    private static string EffectAtRank(HeroUpgradeType type, int rank)
    {
        return type switch
        {
            HeroUpgradeType.MaxHealth => $"Max HP +{12 * rank}%",
            HeroUpgradeType.Damage => $"Attack damage +{10 * rank}%",
            HeroUpgradeType.AttackSpeed => $"Attack cooldown -{7 * rank}%",
            HeroUpgradeType.MoveSpeed => $"Move speed +{6 * rank}%",
            HeroUpgradeType.Armor => $"Damage reduction +{4 * rank}%",
            HeroUpgradeType.PickupRange => $"EXP pickup radius +{20 * rank}%",
            HeroUpgradeType.Cleave => $"Nearby targets take {18 + 4 * rank}% attack damage",
            HeroUpgradeType.Knockback => $"Knockback +{1.2f * rank:0.0}",
            HeroUpgradeType.LifeSteal => $"Heal {3 * rank}% of damage dealt",
            HeroUpgradeType.MultiStrike => $"{8 * rank}% chance for a 65% bonus strike",
            HeroUpgradeType.Shockwave => $"Every {Mathf.Max(2, 7 - rank)} hits: area damage",
            HeroUpgradeType.Execution => $"+{10 * rank}% damage vs targets below 25% HP",
            HeroUpgradeType.HealingPulse => $"Heal {8 + 2.5f * rank:0.#}% Max HP every {Mathf.Max(3.5f, 10f - rank * .75f):0.#}s",
            HeroUpgradeType.ArcaneNova => $"Nova: {65 + 22 * rank}% damage every {Mathf.Max(4f, 9f - rank * .65f):0.#}s",
            HeroUpgradeType.InfernoField => $"Burning field: 32% Hero damage every 0.45s for {3.2f + .4f * (rank - 1):0.#}s",
            HeroUpgradeType.OrbitingSaws => $"Orbiting saws: {Mathf.Min(5, 1 + rank)} blades; each deals 58% damage every 0.38s",
            _ => "Survival upgrade"
        };
    }
}
