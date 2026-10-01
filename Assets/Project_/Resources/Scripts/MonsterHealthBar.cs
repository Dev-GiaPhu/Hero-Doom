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

    private Camera targetCamera;
    private float alpha;
    private Vector3 shownScale = Vector3.one;

    void Awake()
    {
        if (monster == null) monster = GetComponentInParent<MonsterAI>();
        if (visualRoot == null) visualRoot = transform as RectTransform;
        if (visualRoot != null) shownScale = visualRoot.localScale;
        targetCamera = Camera.main;
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

        if (fill != null) fill.fillAmount = hp01;

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
