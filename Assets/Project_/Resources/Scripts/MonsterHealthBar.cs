using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MonsterHealthBar : MonoBehaviour
{
    public MonsterAI monster;
    public CanvasGroup canvasGroup;
    [Tooltip("Foreground fill authored in the prefab. It shrinks from left to right as HP falls.")]
    public Image fill;
    public RectTransform visualRoot;
    public Vector3 worldOffset = new Vector3(0f, 2.25f, 0f);
    [Min(0.02f)] public float fadeInDuration = 0.12f;
    [Min(0.02f)] public float fadeOutDuration = 0.16f;
    [Tooltip("Seconds for the visible fill to catch up after damage. Higher values make every hit readable.")]
    [Min(0.05f)] public float fillSlideDuration = 0.48f;
    [Range(0.5f, 1f)] public float hiddenScale = 0.78f;
    public Color healthyColor = new Color(0.25f, 0.9f, 0.35f, 1f);
    public Color woundedColor = new Color(1f, 0.72f, 0.12f, 1f);
    public Color criticalColor = new Color(1f, 0.2f, 0.12f, 1f);

    private Camera targetCamera;
    private float alpha;
    private Vector3 shownScale = Vector3.one;
    private float displayedHp = 1f;
    private float fillVelocity;

    void Awake()
    {
        if (monster == null) monster = GetComponentInParent<MonsterAI>();
        if (visualRoot == null) visualRoot = transform as RectTransform;
        if (visualRoot != null) shownScale = visualRoot.localScale;
        targetCamera = Camera.main;
        displayedHp = monster != null ? Mathf.Clamp01(monster.HPPercent) : 1f;
        if (fill != null)
        {
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = displayedHp;
        }
        ApplyVisual(true);
    }

    void LateUpdate()
    {
        if (monster == null) { Destroy(gameObject); return; }

        float hp01 = Mathf.Clamp01(monster.HPPercent);
        bool shouldShow = monster.IsAlive && hp01 < 0.999f;
        float duration = shouldShow ? fadeInDuration : fadeOutDuration;
        alpha = Mathf.MoveTowards(alpha, shouldShow ? 1f : 0f, Time.deltaTime / Mathf.Max(.02f, duration));

        if (fill != null)
        {
            // SmoothDamp is intentionally slow enough to show a genuine left-to-right loss of fill.
            displayedHp = Mathf.SmoothDamp(displayedHp, hp01, ref fillVelocity, fillSlideDuration, Mathf.Infinity, Time.deltaTime);
            if (Mathf.Abs(displayedHp - hp01) < .002f) displayedHp = hp01;
            fill.fillAmount = displayedHp;
            fill.color = hp01 >= .5f
                ? Color.Lerp(woundedColor, healthyColor, (hp01 - .5f) / .5f)
                : Color.Lerp(criticalColor, woundedColor, hp01 / .5f);
        }

        transform.position = monster.transform.position + worldOffset;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null) transform.rotation = Quaternion.LookRotation(targetCamera.transform.forward, targetCamera.transform.up);
        ApplyVisual(false);
    }

    private void ApplyVisual(bool immediate)
    {
        if (canvasGroup != null) { canvasGroup.alpha = alpha; canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; }
        if (visualRoot != null) visualRoot.localScale = shownScale * Mathf.Lerp(hiddenScale, 1f, alpha);
        if (immediate && canvasGroup != null) canvasGroup.alpha = 0f;
    }
}