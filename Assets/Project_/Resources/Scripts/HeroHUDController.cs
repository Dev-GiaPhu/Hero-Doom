using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HeroHUDController : MonoBehaviour
{
    [Header("UI References")]
    public Slider healthSlider;
    public TMP_Text healthText;

    [Header("Data Source")]
    public HeroDoomGameManager gameManager;

    [Header("Display")]
    public bool showLevel = true;
    public string respawnPrefix = "Respawn";

    void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();
    }

    void Update()
    {
        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<HeroDoomGameManager>();
            if (gameManager == null) return;
        }

        HeroBotAI hero = gameManager.CurrentHero;

        if (hero != null && hero.IsAlive)
        {
            float ratio = hero.MaxHP <= 0f
                ? 0f
                : hero.CurrentHP / hero.MaxHP;

            if (healthSlider != null)
                healthSlider.value = Mathf.Clamp01(ratio);

            if (healthText != null)
            {
                string level = showLevel
                    ? $"  Lv.{hero.Level}"
                    : string.Empty;

                healthText.text =
                    $"{Mathf.CeilToInt(hero.CurrentHP)}/" +
                    $"{Mathf.CeilToInt(hero.MaxHP)}" +
                    level;
            }

            return;
        }

        if (healthSlider != null)
            healthSlider.value = 0f;

        if (healthText != null)
        {
            healthText.text = gameManager.IsRespawning
                ? $"{respawnPrefix} {gameManager.RespawnRemaining:0.0}s"
                : "Hero defeated";
        }
    }
}
