using System.Collections.Generic;
using UnityEngine;

public class ExperienceOrb : MonoBehaviour
{
    public static readonly HashSet<ExperienceOrb> Active = new();

    [Min(0.1f)] public float value = 3f;
    public bool IsCollected { get; private set; }

    void OnEnable()
    {
        IsCollected = false;
        Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
    }

    public void Collect(HeroBotAI hero)
    {
        if (IsCollected || hero == null || !hero.IsAlive)
            return;

        IsCollected = true;
        Active.Remove(this);
        hero.AddExperience(value);
        Destroy(gameObject);
    }

    public static void Spawn(ExperienceOrb prefab, Vector3 position, float amount)
    {
        if (prefab == null) return;
        ExperienceOrb xp = Instantiate(prefab, position + Vector3.up * 0.3f, Quaternion.identity);
        xp.value = Mathf.Max(1f, amount);
    }

    void Update()
    {
        transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
    }
}
