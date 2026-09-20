using System.Collections.Generic;
using UnityEngine;

public class CapturePillar : MonoBehaviour
{
    public float captureRadius = 4f;
    public float goldBonus = 0.25f;
    public readonly HashSet<MonsterAI> guards = new();

    public bool IsCaptured
    {
        get
        {
            guards.RemoveWhere(m => m == null || !m.IsAlive || m.GuardPillar != this);
            return guards.Count > 0;
        }
    }

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
}
