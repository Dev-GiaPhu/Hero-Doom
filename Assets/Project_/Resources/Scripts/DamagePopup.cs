using TMPro;
using UnityEngine;

public enum DamagePopupKind
{
    DamageToHero,
    DamageToMonster,
    Healing
}

public class DamagePopup : MonoBehaviour
{
    private const float Lifetime = 0.85f;

    private TextMeshPro text;
    private Camera targetCamera;
    private float age;
    private Vector3 baseScale;
    private Color baseColor;

    public static void Spawn(
        Vector3 worldPosition,
        float amount,
        DamagePopupKind kind,
        bool emphasized = false)
    {
        if (amount <= 0.01f)
            return;

        GameObject go =
            new GameObject(
                "DamagePopup",
                typeof(RectTransform),
                typeof(TextMeshPro));

        go.transform.position = worldPosition;

        TextMeshPro tmp =
            go.GetComponent<TextMeshPro>();

        tmp.font =
            TMP_Settings.defaultFontAsset;

        tmp.text =
            Mathf.Max(1, Mathf.RoundToInt(amount)).ToString();

        tmp.alignment =
            TextAlignmentOptions.Center;

        tmp.fontStyle =
            emphasized
                ? FontStyles.Bold
                : FontStyles.Normal;

        tmp.fontSize =
            emphasized
                ? 5.2f
                : 4.2f;

        tmp.enableWordWrapping = false;

        Color color;

        switch (kind)
        {
            case DamagePopupKind.DamageToHero:
                color = new Color(1f, 0.34f, 0.24f, 1f);
                break;

            case DamagePopupKind.Healing:
                color = new Color(0.38f, 1f, 0.48f, 1f);
                break;

            default:
                color = emphasized
                    ? new Color(1f, 0.76f, 0.20f, 1f)
                    : new Color(1f, 0.91f, 0.58f, 1f);
                break;
        }

        tmp.color = color;

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.sizeDelta =
            new Vector2(8f, 2f);

        go.transform.localScale =
            Vector3.one * (emphasized ? 0.19f : 0.16f);

        if (tmp.renderer != null)
            tmp.renderer.sortingOrder = 80;

        DamagePopup popup =
            go.AddComponent<DamagePopup>();

        popup.text = tmp;
        popup.targetCamera = Camera.main;
        popup.baseScale = go.transform.localScale;
        popup.baseColor = color;
    }

    void Update()
    {
        age += Time.deltaTime;

        float normalized =
            Mathf.Clamp01(age / Lifetime);

        transform.position +=
            (Vector3.up * 1.15f + Vector3.right * 0.08f)
            * Time.deltaTime;

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera != null)
            transform.rotation = targetCamera.transform.rotation;

        float punch =
            1f + Mathf.Sin(normalized * Mathf.PI) * 0.22f;

        transform.localScale =
            baseScale * punch;

        if (text != null)
        {
            Color c = baseColor;
            c.a = 1f - Mathf.SmoothStep(0f, 1f, normalized);
            text.color = c;
        }

        if (age >= Lifetime)
            Destroy(gameObject);
    }
}
