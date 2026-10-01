using TMPro;
using UnityEngine;

/// <summary>Authored world-space decision display for the Hero's autonomous upgrade choice.</summary>
public class HeroUpgradeThought : MonoBehaviour
{
    [Header("Thought bubble and result")]
    public SpriteRenderer upgradeIcon;
    public TMP_Text caption;
    public Sprite thinkingIcon;
    public float thinkingDuration = 1.25f;
    public float resultDuration = 2.3f;
    public Vector3 worldOffset = new(0f, 3.1f, 0f);
    [Range(0.5f, 2f)] public float iconScale = 0.95f;
    [Range(1f, 1.8f)] public float resultPopScale = 1.32f;

    [Header("Two authored upgrade cards")]
    public SpriteRenderer choiceCardA;
    public SpriteRenderer choiceCardB;
    public SpriteRenderer choiceIconA;
    public SpriteRenderer choiceIconB;
    public TMP_Text choiceTextA;
    public TMP_Text choiceTextB;

    public HeroUpgradeDefinition OfferedA => offerA;
    public HeroUpgradeDefinition OfferedB => offerB;
    public HeroUpgradeDefinition Selected => choice;

    private Transform follow;
    private HeroUpgradeDefinition offerA;
    private HeroUpgradeDefinition offerB;
    private HeroUpgradeDefinition choice;
    private Camera targetCamera;
    private float age;
    private float resolveAge;
    private int rank;
    private Vector3 cardAPosition;
    private Vector3 cardBPosition;
    private Vector3 cardAScale;
    private Vector3 cardBScale;

    public void Show(Transform target, HeroUpgradeDefinition first, HeroUpgradeDefinition second, float decisionTime)
    {
        follow = target;
        offerA = first;
        offerB = second;
        choice = null;
        age = 0f;
        resolveAge = 0f;
        thinkingDuration = Mathf.Max(0.2f, decisionTime);
        targetCamera = Camera.main;

        if (upgradeIcon != null)
        {
            upgradeIcon.sprite = thinkingIcon;
            upgradeIcon.enabled = true;
            upgradeIcon.transform.localPosition = new Vector3(0f, .63f, 0f);
            upgradeIcon.transform.localScale = Vector3.one * iconScale * .68f;
        }
        if (caption != null)
        {
            caption.text = "...";
            caption.gameObject.SetActive(true);
        }
        SetOffer(choiceCardA, choiceIconA, choiceTextA, offerA);
        SetOffer(choiceCardB, choiceIconB, choiceTextB, offerB);
        CacheCardPose(choiceCardA, new Vector3(-.56f, -.28f, 0f), out cardAPosition, out cardAScale);
        CacheCardPose(choiceCardB, new Vector3(.56f, -.28f, 0f), out cardBPosition, out cardBScale);
    }

    public void Resolve(HeroUpgradeDefinition selected, int selectedRank)
    {
        if (selected == null) return;
        choice = selected;
        rank = selectedRank;
        resolveAge = 0f;
    }

    private void LateUpdate()
    {
        if (follow == null) { Destroy(gameObject); return; }
        age += Time.unscaledDeltaTime;
        transform.position = follow.position + worldOffset;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null) transform.rotation = targetCamera.transform.rotation;

        if (choice == null)
        {
            float pulse = 1f + Mathf.Sin(age * 5.5f) * .035f;
            if (choiceCardA != null) choiceCardA.transform.localScale = cardAScale * pulse;
            if (choiceCardB != null) choiceCardB.transform.localScale = cardBScale * (2f - pulse);
            return;
        }

        resolveAge += Time.unscaledDeltaTime;
        AnimateCard(choiceCardA, choice == offerA, cardAPosition, cardAScale);
        AnimateCard(choiceCardB, choice == offerB, cardBPosition, cardBScale);

        float selectionT = Mathf.Clamp01(resolveAge / .48f);
        if (selectionT >= 1f)
        {
            if (choiceCardA != null) choiceCardA.gameObject.SetActive(false);
            if (choiceCardB != null) choiceCardB.gameObject.SetActive(false);
            if (upgradeIcon != null)
            {
                upgradeIcon.sprite = choice.icon != null ? choice.icon : thinkingIcon;
                upgradeIcon.enabled = true;
                float popT = Mathf.Clamp01((resolveAge - .48f) / .22f);
                float pop = Mathf.Lerp(resultPopScale, 1f, Mathf.SmoothStep(0f, 1f, popT));
                upgradeIcon.transform.localScale = Vector3.one * iconScale * pop;
            }
            if (caption != null) caption.text = $"{choice.displayName}  x{rank}";
            if (resolveAge >= resultDuration) Destroy(gameObject);
        }
    }

    private void AnimateCard(SpriteRenderer card, bool selected, Vector3 basePosition, Vector3 baseScale)
    {
        if (card == null) return;
        float t = Mathf.Clamp01(resolveAge / .48f);
        if (selected)
        {
            card.transform.localPosition = Vector3.Lerp(basePosition, new Vector3(0f, -.22f, 0f), Mathf.SmoothStep(0f, 1f, t));
            card.transform.localScale = baseScale * Mathf.Lerp(1f, 1.2f, t);
            SetAlpha(card, Mathf.Lerp(1f, 0f, Mathf.Clamp01((t - .72f) / .28f)));
        }
        else
        {
            card.transform.localPosition = basePosition + Vector3.down * (.2f * t);
            card.transform.localScale = baseScale * Mathf.Lerp(1f, .72f, t);
            SetAlpha(card, 1f - t);
        }
    }

    private static void SetOffer(SpriteRenderer card, SpriteRenderer icon, TMP_Text label, HeroUpgradeDefinition definition)
    {
        if (card != null) card.gameObject.SetActive(definition != null);
        if (icon != null)
        {
            icon.sprite = definition != null ? definition.icon : null;
            icon.gameObject.SetActive(definition != null && definition.icon != null);
        }
        if (label != null)
        {
            label.text = definition != null ? $"{(definition.category == HeroUpgradeCategory.Skill ? "SKILL" : "STAT")}  {definition.displayName}\n{definition.description}" : string.Empty;
            label.gameObject.SetActive(definition != null);
        }
    }

    private static void CacheCardPose(SpriteRenderer card, Vector3 position, out Vector3 cachedPosition, out Vector3 cachedScale)
    {
        cachedPosition = position;
        cachedScale = Vector3.one;
        if (card == null) return;
        card.gameObject.SetActive(true);
        card.transform.localPosition = position;
        cachedScale = card.transform.localScale;
    }

    private static void SetAlpha(SpriteRenderer renderer, float alpha)
    {
        Color color = renderer.color;
        color.a = alpha;
        renderer.color = color;
    }
}
