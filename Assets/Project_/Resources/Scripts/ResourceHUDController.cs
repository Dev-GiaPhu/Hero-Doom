using TMPro;
using UnityEngine;

public class ResourceHUDController : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Hiển thị vàng hiện có của người triệu hồi.")]
    public TMP_Text coinText;
    [Tooltip("Dòng nằm dưới vàng. Đây là số Rebirth Point dùng để mua node trong Rebirth Tree.")]
    public TMP_Text soulText;

    [Header("Data Source")]
    public HeroDoomGameManager gameManager;
    [Tooltip("Nguồn Rebirth Point. Nếu để trống, controller sẽ tự lấy SummonerUpgrades trong scene.")]
    public SummonerUpgrades upgrades;

    void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();
        if (upgrades == null)
            upgrades = gameManager != null && gameManager.playerSpawner != null
                ? gameManager.playerSpawner.upgrades
                : FindFirstObjectByType<SummonerUpgrades>();

        Refresh();
    }

    void Update()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();
        if (upgrades == null)
            upgrades = gameManager != null && gameManager.playerSpawner != null
                ? gameManager.playerSpawner.upgrades
                : FindFirstObjectByType<SummonerUpgrades>();

        Refresh();
    }

    public void Refresh()
    {
        if (coinText != null)
        {
            double gold =
                gameManager != null
                    ? gameManager.Gold
                    : 0d;

            coinText.text =
                HeroDoomGameManager.FormatNumber(gold);
        }

        if (soulText != null)
            soulText.text = (upgrades != null ? upgrades.RebirthPoints : 0).ToString();
    }
}
