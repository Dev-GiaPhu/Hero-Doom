using UnityEngine;

[DisallowMultipleComponent]
public class SpawnProtection : MonoBehaviour
{
    [Min(0)] public float duration = 2f;
    [Tooltip("Authored child object, visible in Prefab Mode and hidden when protection expires.")]
    public GameObject ringVisual;
    public Renderer ringRenderer;
    public float rotationSpeed = 45f;
    [Range(0.05f, 1f)] public float fadeOutDuration = 0.4f;
    public bool IsProtected => isActiveAndEnabled && Time.time < expiresAt;
    public float Remaining => Mathf.Max(0, expiresAt - Time.time);
    private float expiresAt;
    private MaterialPropertyBlock properties;

    void OnEnable() { Begin(); }

    public void Begin()
    {
        expiresAt = Time.time + Mathf.Max(0, duration);
        if (ringVisual != null) ringVisual.SetActive(duration > 0);
        UpdateVisual();
    }

    void Update()
    {
        if (ringVisual == null) return;
        ringVisual.SetActive(IsProtected);
        if (!IsProtected) return;
        ringVisual.transform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime, Space.Self);
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (ringRenderer == null) return;
        properties ??= new MaterialPropertyBlock();
        ringRenderer.GetPropertyBlock(properties);
        properties.SetFloat("_Opacity", Mathf.Clamp01(Remaining / Mathf.Max(0.05f, fadeOutDuration)));
        ringRenderer.SetPropertyBlock(properties);
    }

    public void End()
    {
        expiresAt = 0;
        if (ringVisual != null) ringVisual.SetActive(false);
    }

    void OnDisable() { End(); }
}
