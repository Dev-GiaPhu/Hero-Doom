using UnityEngine;

public class WaterHazard : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        HeroBotAI hero = other.GetComponentInParent<HeroBotAI>();
        if (hero != null && hero.IsAlive) { hero.KillByEnvironment(); return; }

        MonsterAI monster = other.GetComponentInParent<MonsterAI>();
        if (monster != null && monster.IsAlive) monster.KillByEnvironment();
    }
}
