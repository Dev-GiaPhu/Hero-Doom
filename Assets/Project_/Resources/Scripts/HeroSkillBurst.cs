using UnityEngine;
using UnityEngine.VFX;

/// <summary>Plays an authored combat-visual prefab. It can use meshes, materials, particles, or VFX Graph.</summary>
public sealed class HeroSkillBurst : MonoBehaviour
{
    [Tooltip("Optional Visual Effect Graph layers authored on this prefab.")]
    public VisualEffect[] visualEffects;
    [Tooltip("Renderers authored below this prefab. Their own tint is preserved while fading out.")]
    public Renderer[] visualRenderers;
    [Tooltip("Scales and rotates the authored visual root for a short burst.")]
    public bool animateScale = true;
    public Transform animatedRoot;
    [Min(.05f)] public float duration = .6f;
    [Min(0f)] public float startScale = .25f;
    [Min(0f)] public float endScale = 1f;
    public float rotationSpeed;
    public bool destroyAfterPlayback = true;

    private float started;
    private MaterialPropertyBlock propertyBlock;
    private Color[] authoredColors;

    private void Awake()
    {
        started = Time.time;
        if (visualEffects == null) visualEffects = GetComponentsInChildren<VisualEffect>(true);
        if (visualRenderers == null || visualRenderers.Length == 0) visualRenderers = GetComponentsInChildren<Renderer>(true);
        if (animateScale && animatedRoot == null) animatedRoot = transform;
        authoredColors = new Color[visualRenderers.Length];
        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Material material = visualRenderers[i] != null ? visualRenderers[i].sharedMaterial : null;
            authoredColors[i] = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                material != null && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        }
    }

    public void Play()
    {
        started = Time.time;
        foreach (VisualEffect effect in visualEffects ?? System.Array.Empty<VisualEffect>())
            if (effect != null && effect.visualEffectAsset != null) { effect.Reinit(); effect.Play(); }
    }

    public void Play(Vector3 position) { transform.position = position; Play(); }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - started) / Mathf.Max(.05f, duration));
        if (animateScale && animatedRoot != null)
        {
            float scale = Mathf.Lerp(startScale, endScale, Mathf.SmoothStep(0f, 1f, t));
            animatedRoot.localScale = Vector3.one * scale;
            animatedRoot.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
        }

        propertyBlock ??= new MaterialPropertyBlock();
        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer renderer = visualRenderers[i];
            if (renderer == null) continue;
            Color tint = authoredColors[i]; tint.a *= 1f - t;
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", tint);
            propertyBlock.SetColor("_Color", tint);
            renderer.SetPropertyBlock(propertyBlock);
        }
        if (t >= 1f && destroyAfterPlayback) Destroy(gameObject);
    }

    public static HeroSkillBurst Spawn(HeroSkillBurst prefab, Vector3 position)
    {
        return prefab == null ? null : Instantiate(prefab, position, Quaternion.identity);
    }
}