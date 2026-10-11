using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Controls an Editor-authored prize wheel. It never creates UI at runtime.</summary>
public sealed class LuckyWheelController : MonoBehaviour
{
    public GameObject popup;
    public RectTransform wheel;
    public Button openButton;
    public Button closeButton;
    public Button spinButton;
    public TMP_Text balanceText;
    public TMP_Text costText;
    public TMP_Text resultText;
    [Min(1)] public double baseSpinCost = 160d;
    [Min(1f)] public float costGrowth = 1.16f;
    [Min(0.1f)] public float spinDuration = 3f;

    private enum RewardKind { SmallGold, HeroHeal, BasicSummons, StandardGold, HeroExperience, GoldFrenzy, DoubleGold, JackpotGold }
    private static readonly RewardKind[] Rewards = { RewardKind.SmallGold, RewardKind.HeroHeal, RewardKind.BasicSummons, RewardKind.StandardGold, RewardKind.HeroExperience, RewardKind.GoldFrenzy, RewardKind.DoubleGold, RewardKind.JackpotGold };
    // High gold multipliers are rare; most turns grant a useful non-gold reward.
    private static readonly int[] Weights = { 22, 20, 18, 15, 9, 8, 6, 2 };
    private static readonly string[] RewardLabels = { "0.5x GOLD", "HERO HEAL", "2 MINIONS", "1x GOLD", "HERO EXP", "GOLD FRENZY", "2x GOLD", "5x JACKPOT" };
    private HeroDoomGameManager manager;
    private bool spinning;
    private int completedSpins;

    private double CurrentCost => baseSpinCost * System.Math.Pow(costGrowth, completedSpins);

    private void Awake()
    {
        manager = FindFirstObjectByType<HeroDoomGameManager>();
        if (openButton != null) openButton.onClick.AddListener(TogglePopup);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePopup);
        if (spinButton != null) spinButton.onClick.AddListener(Spin);
        if (popup != null) popup.SetActive(false);
    }

    private void OnDestroy()
    {
        if (openButton != null) openButton.onClick.RemoveListener(TogglePopup);
        if (closeButton != null) closeButton.onClick.RemoveListener(ClosePopup);
        if (spinButton != null) spinButton.onClick.RemoveListener(Spin);
    }

    private void Update()
    {
        if (manager == null) manager = FindFirstObjectByType<HeroDoomGameManager>();
        double gold = manager != null ? manager.Gold : 0d;
        if (balanceText != null) balanceText.text = $"GOLD  {HeroDoomGameManager.FormatNumber(gold)}";
        if (costText != null) costText.text = $"SPIN  {HeroDoomGameManager.FormatNumber(CurrentCost)} G";
        if (spinButton != null) spinButton.interactable = !spinning && manager != null && manager.CanAfford(CurrentCost);
    }

    public void TogglePopup()
    {
        if (popup == null) return;
        bool show = !popup.activeSelf;
        popup.SetActive(show);
        if (show) popup.transform.SetAsLastSibling();
        if (show && resultText != null) resultText.text = "CHOOSE YOUR FATE";
    }

    public void ClosePopup()
    {
        if (!spinning && popup != null) popup.SetActive(false);
    }

    public void Spin()
    {
        if (spinning || manager == null || wheel == null || !manager.SpendGold(CurrentCost)) return;
        GameAudioController.Instance?.PlayClick();
        StartCoroutine(SpinRoutine());
    }

    private IEnumerator SpinRoutine()
    {
        spinning = true;
        int slice = RollSlice();
        double paid = CurrentCost;
        float start = wheel.localEulerAngles.z;
        float landing = 22.5f + slice * 45f;
        float delta = Mathf.Repeat(landing - start, 360f);
        float target = start + 360f * Random.Range(5, 8) + delta;
        float elapsed = 0f;
        while (elapsed < spinDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / spinDuration);
            float eased = 1f - Mathf.Pow(1f - t, 4f);
            wheel.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(start, target, eased));
            yield return null;
        }
        wheel.localRotation = Quaternion.Euler(0f, 0f, target);
        double reward = 0d;
        RewardKind rewardKind = Rewards[slice];
        switch (rewardKind)
        {
            case RewardKind.SmallGold: reward = paid * .5d; manager.GrantGold(reward); break;
            case RewardKind.StandardGold: reward = paid; manager.GrantGold(reward); break;
            case RewardKind.DoubleGold: reward = paid * 2d; manager.GrantGold(reward); break;
            case RewardKind.JackpotGold: reward = paid * 5d; manager.GrantGold(reward); break;
            case RewardKind.HeroHeal:
                HeroDoomGameManager.Instance?.CurrentHero?.HealFraction(.35f);
                break;
            case RewardKind.BasicSummons:
                for (int i = 0; i < 2; i++)
                    if (manager.playerSpawner != null) manager.playerSpawner.TryAutoSpawn(0, false);
                break;
            case RewardKind.HeroExperience:
                HeroBotAI hero = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.CurrentHero : null;
                hero?.AddExperience(hero.ExperienceToNext * .25f);
                break;
            case RewardKind.GoldFrenzy:
                manager.ApplyTimedGoldBoost(2f, 35f);
                break;
        }
        completedSpins++;
        GameAudioController.Instance?.PlayReward();
        if (resultText != null)
            resultText.text = reward > 0d
                ? rewardKind == RewardKind.JackpotGold ? $"JACKPOT!  +{HeroDoomGameManager.FormatNumber(reward)} G" : $"YOU WON  +{HeroDoomGameManager.FormatNumber(reward)} G"
                : rewardKind switch
                {
                    RewardKind.HeroHeal => "HERO RESTORED  +35% HP",
                    RewardKind.BasicSummons => "TWO MINIONS JOINED",
                    RewardKind.HeroExperience => "HERO GAINED 25% EXP",
                    _ => "GOLD FRENZY  2x FOR 35s"
                };
        spinning = false;
    }

    public void ResetForRebirth()
    {
        completedSpins = 0;
        spinning = false;
        StopAllCoroutines();
        if (wheel != null) wheel.localRotation = Quaternion.identity;
        if (popup != null) popup.SetActive(false);
    }

    private static int RollSlice()
    {
        int total = 0;
        foreach (int weight in Weights) total += weight;
        int roll = Random.Range(0, total);
        for (int i = 0; i < Weights.Length; i++)
        {
            if (roll < Weights[i]) return i;
            roll -= Weights[i];
        }
        return Weights.Length - 1;
    }
}
