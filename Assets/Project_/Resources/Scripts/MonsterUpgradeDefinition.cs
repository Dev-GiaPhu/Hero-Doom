using UnityEngine;

[CreateAssetMenu(menuName = "Hero Doom/Monster Upgrade")]
public class MonsterUpgradeDefinition : ScriptableObject
{
    public MonsterRole role;
    public string displayName;
    public Sprite icon;
    [Header("Power - gold per rank = base x growth ^ rank")]
    [Min(1)] public int maxPowerLevel = 10;
    [Min(1)] public float basePowerCost = 50;
    [Min(1)] public float powerCostGrowth = 1.6f;
    [Min(0)] public float healthPerLevel = 0.15f;
    [Min(0)] public float damagePerLevel = 0.12f;
    [Header("Automatic summoning")]
    [Min(1)] public int maxAutoLevel = 6;
    [Min(1)] public float baseAutoCost = 100;
    [Min(1)] public float autoCostGrowth = 1.7f;
    [Min(1)] public float startingInterval = 18;
    [Range(0.1f, 1)] public float intervalMultiplier = 0.85f;
    [Min(1)] public float minimumInterval = 4;
    [Min(0)] public int previousMonsterAutoLevelRequired = 3;
    [Min(1)] public int maxAlive = 18;
    [Tooltip("Off: purchasing automation pays for timed summons. On: every summon also costs gold.")]
    public bool chargeGoldPerAutoSummon;
    public double PowerCost(int level) => basePowerCost * System.Math.Pow(powerCostGrowth, level);
    public double AutoCost(int level) => baseAutoCost * System.Math.Pow(autoCostGrowth, level);
    public float Interval(int level) => Mathf.Max(minimumInterval, startingInterval * Mathf.Pow(intervalMultiplier, Mathf.Max(0, level - 1)));
}
