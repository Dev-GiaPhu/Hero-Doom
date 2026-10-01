using UnityEngine;
using UnityEngine.VFX;

/// <summary>Runs an Editor-authored Visual Effect Graph prefab for a short impact/skill cue.</summary>
public class HeroSkillBurst : MonoBehaviour
{
    [Tooltip("Visual Effect Graph components authored on this prefab.")]
    public VisualEffect[] visualEffects;
    [Min(0.1f)] public float duration = 1f;
    public bool destroyAfterPlayback = true;
    private float age;

    private void Awake()
    {
        if (visualEffects == null || visualEffects.Length == 0)
            visualEffects = GetComponentsInChildren<VisualEffect>(true);
    }

    public void Play(Vector3 position)
    {
        transform.position = position;
        age = 0f;
        foreach (VisualEffect effect in visualEffects)
        {
            if (effect == null || effect.visualEffectAsset == null) continue;
            effect.Reinit();
            effect.gameObject.SetActive(true);
            effect.Play();
            effect.SendEvent("OnPlay");
        }
    }

    private void Update()
    {
        if (!destroyAfterPlayback) return;
        age += Time.deltaTime;
        if (age < Mathf.Max(.1f, duration)) return;
        foreach (VisualEffect effect in visualEffects) if (effect != null) effect.Stop();
        Destroy(gameObject);
    }

    public static HeroSkillBurst Spawn(HeroSkillBurst prefab, Vector3 position)
    {
        if (prefab == null) return null;
        HeroSkillBurst instance = Instantiate(prefab, position, prefab.transform.rotation);
        instance.Play(position);
        return instance;
    }
}
