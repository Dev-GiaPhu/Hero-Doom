using TMPro;
using UnityEngine;

public class HeroUpgradeThought : MonoBehaviour
{
    public SpriteRenderer upgradeIcon;
    public TMP_Text caption;
    public Sprite thinkingIcon;
    [Min(0)] public float thinkingDuration = 0.65f;
    [Min(0.2f)] public float resultDuration = 3.0f;
    public Vector3 worldOffset = new Vector3(0, 3.5f, 0);
    [Range(0.5f, 4f)] public float iconScale = 1.9f;
    [Range(1f, 1.6f)] public float resultPopScale = 1.22f;
    private Transform follow;
    private HeroUpgradeDefinition choice;
    private Camera targetCamera;
    private float age;
    private int rank;

    public void Show(Transform target, HeroUpgradeDefinition selected, int level)
    {
        follow = target;
        choice = selected;
        rank = level;
        age = 0;
        targetCamera = Camera.main;
        if (upgradeIcon != null)
        {
            upgradeIcon.sprite = thinkingIcon;
            upgradeIcon.transform.localScale = Vector3.one * iconScale;
        }
        if (caption != null) caption.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (follow == null) { Destroy(gameObject); return; }
        age += Time.deltaTime;
        transform.position = follow.position + worldOffset;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null) transform.rotation = targetCamera.transform.rotation;
        if (age >= thinkingDuration && choice != null)
        {
            if (upgradeIcon != null)
            {
                upgradeIcon.sprite = choice.icon;
                float resultAge = age - thinkingDuration;
                float popT = Mathf.Clamp01(resultAge / 0.18f);
                float scale = Mathf.Lerp(resultPopScale, 1f, Mathf.SmoothStep(0f, 1f, popT));
                upgradeIcon.transform.localScale = Vector3.one * iconScale * scale;
            }
            // Design intent: only the thought/upgrade icon is visible above the Hero.
            // The chosen upgrade name stays hidden to keep the battlefield readable.
            if (caption != null) caption.gameObject.SetActive(false);
        }
        if (age >= thinkingDuration + resultDuration) Destroy(gameObject);
    }
}
