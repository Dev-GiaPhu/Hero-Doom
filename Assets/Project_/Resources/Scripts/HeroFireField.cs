using UnityEngine;

/// <summary>Timed ground hazard spawned from an authored Visual Effect Graph prefab.</summary>
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
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
        {
            if (monster == null || !monster.IsAlive) continue;
            Vector3 delta = monster.transform.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > radiusSqr) continue;
            monster.TakeDamage(damage, true);
            if (++hits >= maxTargetsPerPulse) break;
        }
    }
}
