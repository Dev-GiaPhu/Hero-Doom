using UnityEngine;
using UnityEngine.VFX;

[DisallowMultipleComponent]
public class SpawnProtection : MonoBehaviour
{
    [Min(0)] public float duration = 2f;
    [Tooltip("Authored child object, visible in Prefab Mode and hidden when protection expires.")]
    public GameObject ringVisual;
    public Renderer ringRenderer;
    [Tooltip("Second authored sphere shell, used as a visible transparent energy bubble beneath the Shader Graph layer.")]
    public Renderer energyShellRenderer;
    [Tooltip("Authored Visual Effect Graph that adds moving energy motes around the Shader Graph sphere.")]
    public VisualEffect energyGraphEffect;
    public float rotationSpeed = 45f;
    [Range(0.05f, 1f)] public float fadeOutDuration = 0.4f;
    [Range(0.05f, 0.6f)] public float introPulseDuration = 0.22f;
    [Range(1f, 1.5f)] public float introPulseScale = 1.18f;
    public bool IsProtected => isActiveAndEnabled && Time.time < expiresAt;
    public float Remaining => Mathf.Max(0, expiresAt - Time.time);
    private float expiresAt;
    private MaterialPropertyBlock properties;
    private MaterialPropertyBlock shellProperties;
    private Vector3 ringBaseScale = Vector3.one;
    private float startedAt;
    private float authoredDuration;

    void Awake()
    {
        authoredDuration = duration;
        if (ringVisual != null) ringBaseScale = ringVisual.transform.localScale;
    }

    /// <summary>Applies a permanent summoner bonus without stacking it each time ordinary upgrades refresh.</summary>
    public void SetBonusDuration(float bonusSeconds)
    {
        duration = Mathf.Max(0f, authoredDuration + Mathf.Max(0f, bonusSeconds));
        if (IsProtected) Begin();
    }

    void OnEnable() { Begin(); }

    public void Begin()
    {
        startedAt = Time.time;
        expiresAt = Time.time + Mathf.Max(0, duration);
        if (ringVisual != null)
        {
            ringBaseScale = ringVisual.transform.localScale;
            ringVisual.transform.localScale = ringBaseScale * 0.72f;
            ringVisual.SetActive(duration > 0);
        }
        if (energyGraphEffect != null && duration > 0f && energyGraphEffect.visualEffectAsset != null)
        {
            energyGraphEffect.Reinit(); energyGraphEffect.Play(); energyGraphEffect.SendEvent("OnPlay");
        }
        UpdateVisual();
    }

    void Update()
    {
        if (ringVisual == null) return;
        ringVisual.SetActive(IsProtected);
        if (!IsProtected) return;
        ringVisual.transform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime, Space.Self);

        float intro = Mathf.Clamp01((Time.time - startedAt) / Mathf.Max(0.05f, introPulseDuration));
        float pulse;
        if (intro < 0.55f)
            pulse = Mathf.Lerp(0.72f, introPulseScale, Mathf.SmoothStep(0f, 1f, intro / 0.55f));
        else
            pulse = Mathf.Lerp(introPulseScale, 1f, Mathf.SmoothStep(0f, 1f, (intro - 0.55f) / 0.45f));
        ringVisual.transform.localScale = ringBaseScale * pulse;

        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (ringRenderer == null) return;
        properties ??= new MaterialPropertyBlock();
        ringRenderer.GetPropertyBlock(properties);
        properties.SetFloat("_Opacity", Mathf.Clamp01(Remaining / Mathf.Max(0.05f, fadeOutDuration)));
        ringRenderer.SetPropertyBlock(properties);
        if (energyShellRenderer != null)
        {
            shellProperties ??= new MaterialPropertyBlock();
            energyShellRenderer.GetPropertyBlock(shellProperties);
            Color shellColor = new Color(0.12f, 0.82f, 1f, Mathf.Lerp(0.02f, 0.16f, Mathf.Clamp01(Remaining / Mathf.Max(0.05f, fadeOutDuration))));
            shellProperties.SetColor("_BaseColor", shellColor);
            shellProperties.SetColor("_Color", shellColor);
            energyShellRenderer.SetPropertyBlock(shellProperties);
        }
    }

    public void End()
    {
        expiresAt = 0;
        if (energyGraphEffect != null) energyGraphEffect.Stop();
        if (ringVisual != null)
        {
            ringVisual.transform.localScale = ringBaseScale;
            ringVisual.SetActive(false);
        }
    }

    void OnDisable() { End(); }
}
