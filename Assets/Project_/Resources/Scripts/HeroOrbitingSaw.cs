using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>One authored saw prefab orbiting the Hero and striking nearby monsters.</summary>
public sealed class HeroOrbitingSaw : MonoBehaviour
{
    public VisualEffect graphAccent;
    [Min(.1f)] public float orbitRadius = 2.35f;
    [Min(1f)] public float angularSpeed = 145f;
    [Min(.05f)] public float damageInterval = .38f;
    [Min(.1f)] public float damageFactor = .58f;
    public Vector3 heightOffset = new(0f, .9f, 0f);
    private HeroBotAI owner;
    private int index;
    private int bladeCount = 1;
    private float nextHit;
    // Snapshot prevents TakeDamage/Die from invalidating ActiveMonsters' HashSet enumerator.
    private readonly List<MonsterAI> hitTargets = new(64);

    public void Initialize(HeroBotAI hero, int bladeIndex, int count)
    {
        owner = hero;
        index = bladeIndex;
        bladeCount = Mathf.Max(1, count);
        nextHit = Time.time + .2f;
        if (graphAccent != null && graphAccent.visualEffectAsset != null)
        {
            graphAccent.Reinit();
            graphAccent.Play();
            graphAccent.SendEvent("OnPlay");
        }
    }

    public void SetBladeCount(int count) => bladeCount = Mathf.Max(1, count);

    private void Update()
    {
        if (owner == null || !owner.IsAlive)
        {
            Destroy(gameObject);
            return;
        }
        float phase = Time.time * angularSpeed * Mathf.Deg2Rad + index * Mathf.PI * 2f / bladeCount;
        float radius = orbitRadius + .09f * (bladeCount - 1);
        Vector3 offset = new(Mathf.Cos(phase) * radius, 0f, Mathf.Sin(phase) * radius);
        transform.position = owner.transform.position + heightOffset + offset;
        transform.rotation = Quaternion.Euler(0f, -phase * Mathf.Rad2Deg + 90f, 0f);
        if (Time.time < nextHit) return;
        nextHit = Time.time + damageInterval;
        hitTargets.Clear();
        hitTargets.AddRange(MonsterAI.ActiveMonsters);
        foreach (MonsterAI monster in hitTargets)
        {
            if (monster == null || !monster.IsAlive) continue;
            Vector3 delta = monster.transform.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= 1.2f * 1.2f)
                monster.TakeDamage(owner.SkillDamage * damageFactor, true);
        }
        hitTargets.Clear();
    }
}
