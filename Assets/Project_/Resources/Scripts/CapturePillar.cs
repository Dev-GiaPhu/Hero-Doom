using System.Collections.Generic;
using UnityEngine;

public enum PillarFaction { Neutral, Summoner, Hero, Contested }

public class CapturePillar : MonoBehaviour
{
    public float captureRadius = 4f;
    public float goldBonus = 0.25f;
    [Min(0.1f)] public float turretCooldown = 1.5f;
    [Min(0f)] public float turretDamage = 10f;
    public float turretRange = 18f;
    public LineRenderer shotVisualPrefab;
    [Header("Faction tint")]
    public Color summonerColor = new Color(0.25f, 0.9f, 0.45f, 1f);
    public Color heroColor = new Color(0.25f, 0.65f, 1f, 1f);
    public Color contestedColor = new Color(1f, 0.58f, 0.18f, 1f);
    public Color neutralColor = new Color(0.72f, 0.75f, 0.78f, 1f);
    public PillarFaction faction = PillarFaction.Neutral;
    public readonly HashSet<MonsterAI> guards = new();
    private float nextShot;
    private Renderer[] factionRenderers;
    private MaterialPropertyBlock factionBlock;

    void Awake()
    {
        factionRenderers = GetComponentsInChildren<Renderer>(true);
        factionBlock = new MaterialPropertyBlock();
        ApplyFactionTint();
    }

    public bool IsCaptured => faction == PillarFaction.Summoner;

    public void RegisterGuard(MonsterAI monster)
    {
        if (monster == null || !monster.IsAlive) return;
        guards.Add(monster);
        monster.AssignGuard(this);
    }

    public void UnregisterGuard(MonsterAI monster)
    {
        if (monster != null) guards.Remove(monster);
    }

    public bool IsInside(Vector3 point, float extra = 0f)
    {
        Vector3 flat = point - transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude <= (captureRadius + extra) * (captureRadius + extra);
    }

    public void RefreshOccupancy(bool heroInside, bool monsterInside, bool forceNeutral = false)
    {
        PillarFaction previous = faction;
        if (forceNeutral) faction = PillarFaction.Neutral;
        else if (heroInside && monsterInside) faction = PillarFaction.Contested;
        else if (heroInside) faction = PillarFaction.Hero;
        else if (monsterInside) faction = PillarFaction.Summoner;
        // Empty pillars keep their last owner, matching a capture point that must be retaken.
        if (previous != faction) ApplyFactionTint();
    }

    private void ApplyFactionTint()
    {
        if (factionRenderers == null) return;
        Color color = faction switch
        {
            PillarFaction.Summoner => summonerColor,
            PillarFaction.Hero => heroColor,
            PillarFaction.Contested => contestedColor,
            _ => neutralColor
        };
        foreach (Renderer item in factionRenderers)
        {
            if (item == null) continue;
            item.GetPropertyBlock(factionBlock);
            factionBlock.SetColor("_BaseColor", color);
            factionBlock.SetColor("_Color", color);
            item.SetPropertyBlock(factionBlock);
        }
    }

    public void TryFire(HeroBotAI hero, IReadOnlyList<MonsterAI> monsters)
    {
        if (Time.time < nextShot || faction == PillarFaction.Contested || faction == PillarFaction.Neutral) return;
        nextShot = Time.time + Mathf.Max(0.1f, turretCooldown);
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        if (faction == PillarFaction.Summoner)
        {
            Vector3 heroDelta = hero != null ? hero.transform.position - transform.position : Vector3.zero;
            heroDelta.y = 0f;
            if (hero != null && hero.IsAlive && heroDelta.sqrMagnitude <= turretRange * turretRange)
            {
                MagicProjectile.SpawnLightning(shotVisualPrefab, origin, hero.transform.position + Vector3.up);
                hero.TakeDamage(turretDamage, null, Vector3.zero, false);
            }
            return;
        }

        MonsterAI target = null;
        float best = turretRange * turretRange;
        foreach (MonsterAI monster in monsters)
        {
            if (monster == null || !monster.IsAlive) continue;
            float distance = (monster.transform.position - transform.position).sqrMagnitude;
            if (distance < best) { best = distance; target = monster; }
        }
        if (target != null)
        {
            MagicProjectile.SpawnLightning(shotVisualPrefab, origin, target.transform.position + Vector3.up);
            target.TakeDamage(turretDamage, true);
        }
    }
}
