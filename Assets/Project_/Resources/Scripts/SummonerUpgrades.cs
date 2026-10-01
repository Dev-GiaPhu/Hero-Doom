using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SummonerUpgrades : MonoBehaviour
{
    public HeroDoomGameManager gameManager;
    public PlayerSpawner spawner;
    public TMPro.TMP_Text armyCountLabel;
    [Header("Unlock and Rebirth")]
    public Button rebirthButton;
    public TMP_Text rebirthLabel;
    [Min(0)] public double rebirthStartingGold = 50d;
    [Min(0f)] public float goldMultiplierPerRebirth = 0.12f;
    [SerializeField] private bool[] unlockedMonsters = { true, false, false, false };
    [SerializeField] private int rebirthCount;
    public int RebirthCount => rebirthCount;
    public float RebirthGoldMultiplier => 1f + rebirthCount * goldMultiplierPerRebirth;
    public int RebirthPowerRequirement => 8 + rebirthCount * 2;
    public int TotalPowerLevels { get { int total = 0; for (int i = 0; i < powerLevels.Length; i++) total += powerLevels[i]; return total; } }
    public int UnlockedMonsterCount { get { int total = 0; for (int i = 0; i < unlockedMonsters.Length; i++) if (unlockedMonsters[i]) total++; return total; } }
    [Tooltip("Order: Basic, Mage, Tank, Assassin. Assets contain all balancing values.")]
    public MonsterUpgradeDefinition[] definitions = new MonsterUpgradeDefinition[4];
    [Min(1)] public int maxAliveMonsters = 36;
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
    public float Remaining(int slot) => slot >= 0 && slot < remaining.Length ? Mathf.Max(0, remaining[slot]) : 0f;

    public int AliveCount(int slot)
    {
        if (slot < 0 || slot >= definitions.Length || definitions[slot] == null) return 0;
        int count = 0;
        MonsterRole role = definitions[slot].role;
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
            if (monster != null && monster.IsAlive && monster.role == role) count++;
        return count;
    }

    public bool IsGlobalArmyFull => MonsterAI.ActiveMonsters.Count >= maxAliveMonsters;

    void Awake()
    {
        if (unlockedMonsters == null || unlockedMonsters.Length != 4)
        {
            bool[] old = unlockedMonsters;
            unlockedMonsters = new bool[4];
            if (old != null) Array.Copy(old, unlockedMonsters, Mathf.Min(old.Length, unlockedMonsters.Length));
        }
        unlockedMonsters[0] = true;
    }

    void Update()
    {
        if (armyCountLabel != null) armyCountLabel.text = $"ARMY  {MonsterAI.ActiveMonsters.Count} / {maxAliveMonsters}";
        if (rebirthButton != null) rebirthButton.interactable = CanRebirth;
        if (rebirthLabel != null)
        {
            int level = gameManager != null && gameManager.CurrentHero != null ? gameManager.CurrentHero.Level : 1;
            rebirthLabel.text = CanRebirth
                ? $"REBIRTH  #{rebirthCount + 1}  •  +{goldMultiplierPerRebirth:P0} GOLD"
                : $"REBIRTH  •  UNLOCK {UnlockedMonsterCount}/4  •  POWER {TotalPowerLevels}/{RebirthPowerRequirement}";
        }
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

    void OnEnable()
    {
        if (rebirthButton != null) rebirthButton.onClick.AddListener(PerformRebirth);
    }

    void OnDisable()
    {
        if (rebirthButton != null) rebirthButton.onClick.RemoveListener(PerformRebirth);
    }

    public bool IsMonsterUnlocked(int slot) => slot >= 0 && slot < unlockedMonsters.Length && unlockedMonsters[slot];
    public double UnlockCost(int slot) => slot >= 0 && slot < definitions.Length && definitions[slot] != null ? definitions[slot].unlockCost : 0d;

    public bool CanUnlock(int slot) => slot > 0 && slot < definitions.Length && !IsMonsterUnlocked(slot) &&
        IsMonsterUnlocked(slot - 1) && gameManager != null && gameManager.CanAfford(UnlockCost(slot));

    public bool UnlockMonster(int slot)
    {
        if (!CanUnlock(slot) || !gameManager.SpendGold(UnlockCost(slot))) return false;
        unlockedMonsters[slot] = true;
        Changed?.Invoke();
        return true;
    }

    public bool CanRebirth => gameManager != null && UnlockedMonsterCount == 4 && TotalPowerLevels >= RebirthPowerRequirement;

    public void PerformRebirth()
    {
        if (!CanRebirth) return;
        rebirthCount++;
        gameManager.ApplyPlayerRebirth(rebirthStartingGold, RebirthGoldMultiplier);
        Changed?.Invoke();
    }

    public void ResetForRebirth()
    {
        Array.Clear(powerLevels, 0, powerLevels.Length);
        Array.Clear(autoLevels, 0, autoLevels.Length);
        for (int i = 0; i < unlockedMonsters.Length; i++) unlockedMonsters[i] = i == 0;
        Array.Clear(remaining, 0, remaining.Length);
        nextAutoSpawn = Time.time + 1f;
        nextAutoSlot = 0;
        Changed?.Invoke();
    }

    public bool GrantFreePowerLevel(int slot)
    {
        if (slot < 0 || slot >= definitions.Length || definitions[slot] == null || !IsMonsterUnlocked(slot)) return false;
        powerLevels[slot] = Mathf.Min(definitions[slot].maxPowerLevel, powerLevels[slot] + 1);
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
            if (monster != null && monster.IsAlive && monster.role == definitions[slot].role) Apply(monster, slot);
        Changed?.Invoke();
        return true;
    }

    public bool IsAutoUnlocked(int slot)
    {
        if (slot <= 0) return true;
        if (slot >= definitions.Length || definitions[slot] == null || definitions[slot - 1] == null) return false;
        return autoLevels[slot - 1] >= definitions[slot].previousMonsterAutoLevelRequired;
    }

    public bool CanBuyPower(int slot) => slot >= 0 && slot < definitions.Length && definitions[slot] != null && gameManager != null &&
        IsMonsterUnlocked(slot) && powerLevels[slot] < int.MaxValue && gameManager.CanAfford(definitions[slot].PowerCost(powerLevels[slot]));

    public bool CanBuyAuto(int slot) => definitions[slot] != null && gameManager != null && IsAutoUnlocked(slot) &&
        IsMonsterUnlocked(slot) && autoLevels[slot] < definitions[slot].maxAutoLevel && gameManager.CanAfford(definitions[slot].AutoCost(autoLevels[slot]));

    public bool BuyPower(int slot)
    {
        if (!IsMonsterUnlocked(slot)) return UnlockMonster(slot);
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
        if (slot < 0 || slot >= definitions.Length) return false;
        if (!IsMonsterUnlocked(slot)) return false;
        if (IsGlobalArmyFull) return false;
        if (definitions[slot] == null) return true;
        return AliveCount(slot) < definitions[slot].maxAlive;
    }

    public void Apply(MonsterAI monster, int slot)
    {
        if (monster == null || definitions[slot] == null) return;
        MonsterUpgradeDefinition d = definitions[slot];
        monster.ApplySummonerUpgrade(1 + d.healthPerLevel * powerLevels[slot], 1 + d.damagePerLevel * powerLevels[slot]);
    }
}
