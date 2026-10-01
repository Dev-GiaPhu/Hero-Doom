using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MonsterUpgradeCard : MonoBehaviour
{
    public SummonerUpgrades upgrades;
    [Range(0, 3)] public int slot;
    public TMP_Text title;
    public TMP_Text stats;
    public TMP_Text powerLabel;
    public TMP_Text autoLabel;
    public TMP_Text autoStatus;
    public Image icon;
    public Image progress;
    public Button powerButton;
    public Button autoButton;
    private float refreshAt;

    void OnEnable()
    {
        if (powerButton != null) powerButton.onClick.AddListener(BuyPower);
        if (autoButton != null) autoButton.onClick.AddListener(BuyAuto);
        Refresh();
    }
    void OnDisable()
    {
        if (powerButton != null) powerButton.onClick.RemoveListener(BuyPower);
        if (autoButton != null) autoButton.onClick.RemoveListener(BuyAuto);
    }
    void Update() { if (Time.unscaledTime >= refreshAt) { refreshAt = Time.unscaledTime + 0.15f; Refresh(); } }
    public void BuyPower() { upgrades?.BuyPower(slot); Refresh(); }
    public void BuyAuto() { upgrades?.BuyAuto(slot); Refresh(); }

    public void Refresh()
    {
        if (upgrades == null || upgrades.definitions.Length <= slot || upgrades.definitions[slot] == null) return;
        MonsterUpgradeDefinition d = upgrades.definitions[slot];
        int p = upgrades.PowerLevel(slot), a = upgrades.AutoLevel(slot);
        if (title != null) title.text = d.displayName;
        if (icon != null) icon.sprite = d.icon;
        int alive = upgrades.AliveCount(slot);
        double summonCost = upgrades.spawner != null ? upgrades.spawner.GetCost(slot) : 0d;
        if (stats != null) stats.text =
            $"SUMMON {HeroDoomGameManager.FormatNumber(summonCost)} G   FIELD {alive}/{d.maxAlive}\n" +
            $"HP +{d.healthPerLevel * p:P0}   ATK +{d.damagePerLevel * p:P0}";
        if (powerLabel != null) powerLabel.text = p >= d.maxPowerLevel ? $"POWER Lv.{p}/{d.maxPowerLevel}  MAX" :
            $"POWER Lv.{p}/{d.maxPowerLevel}  |  {HeroDoomGameManager.FormatNumber(d.PowerCost(p))} G";
        if (autoLabel != null) autoLabel.text = a >= d.maxAutoLevel ? $"AUTO Lv.{a}/{d.maxAutoLevel}  MAX" :
            $"AUTO Lv.{a}/{d.maxAutoLevel}  |  {HeroDoomGameManager.FormatNumber(d.AutoCost(a))} G";
        if (autoStatus != null)
        {
            if (!upgrades.IsAutoUnlocked(slot)) autoStatus.text = $"Requires {upgrades.definitions[slot - 1].displayName} AUTO {d.previousMonsterAutoLevelRequired}";
            else if (a == 0) autoStatus.text = $"Unlock: one summon / {d.startingInterval:0}s";
            else if (upgrades.IsGlobalArmyFull) autoStatus.text = $"Army full {MonsterAI.ActiveMonsters.Count}/{upgrades.maxAliveMonsters} - waiting";
            else if (alive >= d.maxAlive) autoStatus.text = $"{d.displayName} cap {alive}/{d.maxAlive} - waiting";
            else autoStatus.text = $"Next {upgrades.Remaining(slot):0.0}s  |  Every {d.Interval(a):0.0}s";
        }
        if (progress != null) progress.fillAmount = a == 0 ? 0 : 1 - Mathf.Clamp01(upgrades.Remaining(slot) / d.Interval(a));
        if (powerButton != null) powerButton.interactable = upgrades.CanBuyPower(slot);
        if (autoButton != null) autoButton.interactable = upgrades.CanBuyAuto(slot);
    }
}
