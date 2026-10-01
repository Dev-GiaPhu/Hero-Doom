using System;
using UnityEngine;

public class SummonerUpgrades : MonoBehaviour
{
    public HeroDoomGameManager gameManager;
    public PlayerSpawner spawner;
    public TMPro.TMP_Text armyCountLabel;
    [Tooltip("Order: Basic, Mage, Tank, Assassin. Assets contain all balancing values.")]
    public MonsterUpgradeDefinition[] definitions = new MonsterUpgradeDefinition[4];
    [Min(1)] public int maxAliveMonsters = 60;
    [Min(0.1f)] public float minimumTimeBetweenAutoSpawns = 0.4f;
    [Min(0.1f)] public float failedSpawnRetryDelay = 1f;
    [SerializeField] private int[] powerLevels = new int[4];
    [SerializeField] private int[] autoLevels = new int[4];
    private readonly float[] remaining = new float[4];
    private float nextAutoSpawn;
    private int nextAutoSlot;
    public event Action Changed;
    public int PowerLevel(int slot) => powerLevels[slot];
    public int AutoLevel(int slot) => autoLevels[slot];
    public float Remaining(int slot) => Mathf.Max(0, remaining[slot]);

    void Update()
    {
        if (armyCountLabel != null) armyCountLabel.text = $"ARMY  {MonsterAI.ActiveMonsters.Count} / {maxAliveMonsters}";
        for (int i = 0; i < 4; i++)
            if (autoLevels[i] > 0) remaining[i] -= Time.deltaTime;
        if (spawner == null || Time.time < nextAutoSpawn) return;
        for (int n = 0; n < 4; n++)
        {
            int i = (nextAutoSlot + n) % 4;
            if (definitions[i] == null || autoLevels[i] == 0 || remaining[i] > 0) continue;
            bool spawned = spawner.TryAutoSpawn(i, definitions[i].chargeGoldPerAutoSummon);
            remaining[i] = spawned ? definitions[i].Interval(autoLevels[i]) : failedSpawnRetryDelay;
            nextAutoSpawn = Time.time + minimumTimeBetweenAutoSpawns;
            nextAutoSlot = (i + 1) % 4;
            break;
        }
    }

    public bool IsAutoUnlocked(int slot) => slot == 0 || autoLevels[slot - 1] >= definitions[slot].previousMonsterAutoLevelRequired;

    public bool CanBuyPower(int slot) => definitions[slot] != null && gameManager != null &&
        powerLevels[slot] < definitions[slot].maxPowerLevel && gameManager.CanAfford(definitions[slot].PowerCost(powerLevels[slot]));

    public bool CanBuyAuto(int slot) => definitions[slot] != null && gameManager != null && IsAutoUnlocked(slot) &&
        autoLevels[slot] < definitions[slot].maxAutoLevel && gameManager.CanAfford(definitions[slot].AutoCost(autoLevels[slot]));

    public bool BuyPower(int slot)
    {
        if (!CanBuyPower(slot) || !gameManager.SpendGold(definitions[slot].PowerCost(powerLevels[slot]))) return false;
        powerLevels[slot]++;
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
            if (monster != null && monster.IsAlive && monster.role == definitions[slot].role) Apply(monster, slot);
        Changed?.Invoke();
        return true;
    }

    public bool BuyAuto(int slot)
    {
        if (!CanBuyAuto(slot) || !gameManager.SpendGold(definitions[slot].AutoCost(autoLevels[slot]))) return false;
        autoLevels[slot]++;
        float interval = definitions[slot].Interval(autoLevels[slot]);
        remaining[slot] = autoLevels[slot] == 1 ? interval : Mathf.Min(remaining[slot], interval);
        Changed?.Invoke();
        return true;
    }

    public bool CanSpawn(int slot)
    {
        if (MonsterAI.ActiveMonsters.Count >= maxAliveMonsters) return false;
        if (definitions[slot] == null) return true;
        int count = 0;
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
            if (monster != null && monster.IsAlive && monster.role == definitions[slot].role) count++;
        return count < definitions[slot].maxAlive;
    }

    public void Apply(MonsterAI monster, int slot)
    {
        if (monster == null || definitions[slot] == null) return;
        MonsterUpgradeDefinition d = definitions[slot];
        monster.ApplySummonerUpgrade(1 + d.healthPerLevel * powerLevels[slot], 1 + d.damagePerLevel * powerLevels[slot]);
    }
}
