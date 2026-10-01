using TMPro;
using UnityEngine;

public enum DamagePopupKind { DamageToHero, DamageToMonster, Healing }

public class DamagePopup : MonoBehaviour
{
    [Header("Prefab references")]
    public TMP_Text label;
    [Header("Motion")]
    [Min(0.1f)] public float lifetime = 0.85f;
    public float riseSpeed = 1.5f;
    public float horizontalSpread = 0.45f;
    public AnimationCurve scaleOverLife = new AnimationCurve(new Keyframe(0, 0.65f), new Keyframe(0.12f, 1.25f), new Keyframe(0.3f, 1), new Keyframe(1, 0.85f));
    public AnimationCurve alphaOverLife = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.45f, 1), new Keyframe(1, 0));
    [Header("Colors")]
    public Color heroDamageColor = new Color(1, 0.3f, 0.23f);
    public Color monsterDamageColor = new Color(1, 0.9f, 0.55f);
    public Color heavyDamageColor = new Color(1, 0.65f, 0.12f);
    public Color healingColor = new Color(0.3f, 1, 0.5f);
    [Min(1)] public float emphasizedScale = 1.4f;
    private Camera targetCamera;
    private float age;
    private Vector3 baseScale;
    private Vector3 drift;
    private Color baseColor;

    public static void Spawn(DamagePopup prefab, Vector3 position, float amount, DamagePopupKind kind, bool emphasized = false)
    {
        if (prefab == null || amount <= 0.01f) return;
        DamagePopup popup = Instantiate(prefab, position, Quaternion.identity);
        popup.Initialize(amount, kind, emphasized);
    }

    public void Initialize(float amount, DamagePopupKind kind, bool emphasized)
    {
        age = 0;
        targetCamera = Camera.main;
        baseScale = transform.localScale * (emphasized ? emphasizedScale : 1);
        baseColor = kind == DamagePopupKind.Healing ? healingColor :
            kind == DamagePopupKind.DamageToHero ? heroDamageColor : emphasized ? heavyDamageColor : monsterDamageColor;
        drift = (targetCamera != null ? targetCamera.transform.right : Vector3.right) * Random.Range(-horizontalSpread, horizontalSpread);
        if (label != null)
        {
            label.text = (kind == DamagePopupKind.Healing ? "+" : "") + Mathf.Max(1, Mathf.RoundToInt(amount));
            label.color = baseColor;
        }
    }

    void LateUpdate()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / Mathf.Max(0.1f, lifetime));
        transform.position += (Vector3.up * riseSpeed + drift) * Time.deltaTime;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null) transform.rotation = targetCamera.transform.rotation;
        transform.localScale = baseScale * scaleOverLife.Evaluate(t);
        if (label != null) label.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alphaOverLife.Evaluate(t));
        if (t >= 1) Destroy(gameObject);
    }
}
