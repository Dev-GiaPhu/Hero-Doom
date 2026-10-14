using System.Collections.Generic;
using UnityEngine;

/// <summary>Timed ground hazard using an authored visual prefab.</summary>
public sealed class HeroFireField : MonoBehaviour
{
    public HeroSkillBurst visuals;
    [Min(.1f)] public float baseDuration = 3.2f;
    [Min(.1f)] public float baseRadius = 2.7f;
    [Min(.05f)] public float damageInterval = .45f;
    [Min(1)] public int maxTargetsPerPulse = 18;
    private HeroBotAI owner;
    private float radius;
    private float endsAt;
    private float nextPulse;
    // Reused per-field snapshot: killing a monster removes it from ActiveMonsters.
    private readonly List<MonsterAI> pulseTargets = new(64);

    public void Activate(HeroBotAI source, int rank)
    {
        owner = source;
        int skillRank = Mathf.Clamp(rank, 1, 5);
        radius = baseRadius + .45f * (skillRank - 1);
        endsAt = Time.time + baseDuration + .4f * (skillRank - 1);
        if (visuals != null)
        {
            visuals.duration = baseDuration + .4f * (skillRank - 1);
            visuals.Play(transform.position);
        }
        transform.localScale = Vector3.one * (radius * 2f);
        nextPulse = Time.time;
    }

    private void Update()
    {
        if (owner == null || !owner.IsAlive || Time.time >= endsAt)
        {
            if (visuals != null) visuals.destroyAfterPlayback = false;
            Destroy(gameObject);
            return;
        }
        if (Time.time < nextPulse) return;
        nextPulse = Time.time + Mathf.Max(.05f, damageInterval);
        float damage = owner.SkillDamage * .32f;
        float radiusSqr = radius * radius;
        int hits = 0;
        pulseTargets.Clear();
        pulseTargets.AddRange(MonsterAI.ActiveMonsters);
        foreach (MonsterAI monster in pulseTargets)
        {
            if (monster == null || !monster.IsAlive) continue;
            Vector3 delta = monster.transform.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > radiusSqr) continue;
            monster.TakeDamage(damage, true);
            if (++hits >= maxTargetsPerPulse) break;
        }
        pulseTargets.Clear();
    }
}
