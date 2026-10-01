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
    [Tooltip("How quickly the authored fill follows the monster's current HP. Lower values make damage easier to read.")]
    [Min(0.05f)] public float fillSlideSpeed = 1.6f;
    [Range(0.5f, 1f)] public float hiddenScale = 0.78f;
    public Color healthyColor = new Color(0.25f, 0.9f, 0.35f, 1f);
    public Color woundedColor = new Color(1f, 0.72f, 0.12f, 1f);
    public Color criticalColor = new Color(1f, 0.2f, 0.12f, 1f);

    private Camera targetCamera;
    private float alpha;
    private Vector3 shownScale = Vector3.one;
    private float displayedHp = 1f;

    void Awake()
    {
        if (monster == null) monster = GetComponentInParent<MonsterAI>();
        if (visualRoot == null) visualRoot = transform as RectTransform;
        if (visualRoot != null) shownScale = visualRoot.localScale;
        targetCamera = Camera.main;
        displayedHp = monster != null ? Mathf.Clamp01(monster.HPPercent) : 1f;
        if (fill != null) fill.fillAmount = displayedHp;
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

        float duration = shouldShow ? fadeInDuration : fadeOutDuration;
        float speed = 1f / Mathf.Max(0.02f, duration);
        alpha = Mathf.MoveTowards(alpha, shouldShow ? 1f : 0f, speed * Time.deltaTime);

        if (fill != null)
        {
            displayedHp = Mathf.MoveTowards(displayedHp, hp01, fillSlideSpeed * Time.deltaTime);
            fill.fillAmount = displayedHp;
            if (hp01 >= 0.5f)
                fill.color = Color.Lerp(woundedColor, healthyColor, (hp01 - 0.5f) / 0.5f);
            else
                fill.color = Color.Lerp(criticalColor, woundedColor, hp01 / 0.5f);
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
