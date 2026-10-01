using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MonsterHealthBar : MonoBehaviour
{
    public MonsterAI monster;
    public CanvasGroup canvasGroup;
    public Image fill;
    public RectTransform visualRoot;
    public Vector3 worldOffset = new Vector3(0f, 2.25f, 0f);
    [Min(0.02f)] public float fadeInDuration = 0.12f;
    [Min(0.02f)] public float fadeOutDuration = 0.16f;
    [Range(0.5f, 1f)] public float hiddenScale = 0.78f;
    [Min(0.01f)] public float fillSlideSpeed = 3.5f;
    public Color healthyColor = new Color(0.25f, 0.9f, 0.35f, 1f);
    public Color woundedColor = new Color(1f, 0.72f, 0.12f, 1f);
    public Color criticalColor = new Color(1f, 0.2f, 0.12f, 1f);

    private Camera targetCamera;
    private float alpha;
    private float displayedHp = 1f;
    private Vector3 shownScale = Vector3.one;

    void Awake()
    {
        if (monster == null) monster = GetComponentInParent<MonsterAI>();
        if (visualRoot == null) visualRoot = transform as RectTransform;
        if (visualRoot != null) shownScale = visualRoot.localScale;
        targetCamera = Camera.main;
        displayedHp = monster != null ? Mathf.Clamp01(monster.HPPercent) : 1f;
        ApplyVisual(true);
    }

    void LateUpdate()
    {
        if (monster == null)
        {
            Destroy(gameObject);
            return;
        }

        float hp01 = Mathf.Clamp01(monster.HPPercent);
        bool shouldShow = monster.IsAlive && hp01 < 0.999f;

        displayedHp = Mathf.MoveTowards(
            displayedHp,
            hp01,
            Mathf.Max(0.01f, fillSlideSpeed) * Time.deltaTime);

        float duration = shouldShow ? fadeInDuration : fadeOutDuration;
        float speed = 1f / Mathf.Max(0.02f, duration);
        alpha = Mathf.MoveTowards(alpha, shouldShow ? 1f : 0f, speed * Time.deltaTime);

        if (fill != null)
        {
            fill.fillAmount = displayedHp;
            if (displayedHp >= 0.5f)
                fill.color = Color.Lerp(woundedColor, healthyColor, (displayedHp - 0.5f) / 0.5f);
            else
                fill.color = Color.Lerp(criticalColor, woundedColor, displayedHp / 0.5f);
        }

        transform.position = monster.transform.position + worldOffset;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null)
            transform.rotation = Quaternion.LookRotation(targetCamera.transform.forward, targetCamera.transform.up);

        ApplyVisual(false);
    }

    private void ApplyVisual(bool immediate)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (visualRoot != null)
        {
            float scale = Mathf.Lerp(hiddenScale, 1f, alpha);
            visualRoot.localScale = shownScale * scale;
        }

        if (immediate && canvasGroup != null)
            canvasGroup.alpha = 0f;
    }
}
