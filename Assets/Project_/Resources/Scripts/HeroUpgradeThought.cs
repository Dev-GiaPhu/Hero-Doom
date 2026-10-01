using TMPro;
using UnityEngine;

public class HeroUpgradeThought : MonoBehaviour
{
    public SpriteRenderer upgradeIcon;
    public TMP_Text caption;
    public Sprite thinkingIcon;
    [Min(0)] public float thinkingDuration = 0.65f;
    [Min(0.2f)] public float resultDuration = 2.2f;
    public Vector3 worldOffset = new Vector3(0, 3.1f, 0);
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
        if (upgradeIcon != null) upgradeIcon.sprite = thinkingIcon;
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
            if (upgradeIcon != null) upgradeIcon.sprite = choice.icon;
            // Design intent: only the thought/upgrade icon is visible above the Hero.
            // The chosen upgrade name stays hidden to keep the battlefield readable.
            if (caption != null) caption.gameObject.SetActive(false);
        }
        if (age >= thinkingDuration + resultDuration) Destroy(gameObject);
    }
}
