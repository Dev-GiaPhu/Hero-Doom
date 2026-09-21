using TMPro;
using UnityEngine;

public class ResourceHUDController : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text coinText;
    public TMP_Text soulText;

    [Header("Data Source")]
    public HeroDoomGameManager gameManager;

    [Header("Soul - chưa có gameplay, để Inspector quản lý")]
    [Min(0)] public long displayedSouls = 0;

    void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();

        Refresh();
    }

    void Update()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();

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
            soulText.text = displayedSouls.ToString();
    }
}
