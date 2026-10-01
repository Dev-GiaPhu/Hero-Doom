using UnityEngine;
using UnityEngine.VFX;

/// <summary>Weather visuals are authored Visual Effect Graph prefabs; the controller only plays and positions them.</summary>
public sealed class WeatherVFXController : MonoBehaviour
{
    [Header("Scene references")]
    public WeatherManager weather;
    public Camera targetCamera;
    public HeroSkillBurst rainGraphPrefab;
    public HeroSkillBurst splashGraphPrefab;

    [Header("Weather tuning")]
    [Min(.1f)] public float weatherFadeSpeed = .65f;
    public float rainHeight = 16f;
    public float effectRadius = 22f;
    public LayerMask groundMask;
    [Min(0)] public float splashRate = 13f;
    [Min(1)] public int maximumSplashEffectsPerSecond = 24;

    private HeroSkillBurst rainEffect;
    private float rainIntensity;
    private bool rainPlaying;
    private float splashBudget;
    private int splashCountThisSecond;
    private float splashSecondEndsAt;

    private void Start()
    {
        if (weather == null) weather = FindFirstObjectByType<WeatherManager>();
        if (targetCamera == null) targetCamera = Camera.main;
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundMask.value == 0 && groundLayer >= 0) groundMask = 1 << groundLayer;
        if (rainGraphPrefab != null)
        {
            rainEffect = Instantiate(rainGraphPrefab, transform);
            rainEffect.name = "Authored Rain Visual Effect Graph";
            rainEffect.destroyAfterPlayback = false;
            rainEffect.Play(transform.position);
            foreach (VisualEffect vfx in rainEffect.visualEffects) if (vfx != null) vfx.Stop();
        }
    }

    private void Update()
    {
        if (weather == null || targetCamera == null) return;
        float target = weather.CurrentWeather == WeatherType.Rain ? 1f : 0f;
        rainIntensity = Mathf.MoveTowards(rainIntensity, target, weatherFadeSpeed * Time.deltaTime);
        Vector3 camera = targetCamera.transform.position;
        if (rainEffect != null)
        {
            rainEffect.transform.position = new Vector3(camera.x, rainHeight, camera.z + 4f);
            rainEffect.transform.localScale = Vector3.one * Mathf.Max(1f, effectRadius * .12f);
            foreach (VisualEffect vfx in rainEffect.visualEffects)
            {
                if (vfx == null) continue;
                if (rainIntensity > .025f)
                {
                    if (!rainPlaying) { vfx.Reinit(); vfx.Play(); vfx.SendEvent("OnPlay"); }
                    vfx.playRate = rainIntensity;
                }
                else if (rainPlaying) vfx.Stop();
            }
            rainPlaying = rainIntensity > .025f;
        }
        Shader.SetGlobalFloat("_HeroDoomRainIntensity", rainIntensity);
        EmitGroundSplashes(camera);
    }

    private void EmitGroundSplashes(Vector3 camera)
    {
        if (splashGraphPrefab == null || rainIntensity <= .01f) return;
        if (Time.time >= splashSecondEndsAt)
        {
            splashSecondEndsAt = Time.time + 1f;
            splashCountThisSecond = 0;
        }
        splashBudget += rainIntensity * splashRate * Time.deltaTime;
        while (splashBudget >= 1f && splashCountThisSecond < maximumSplashEffectsPerSecond)
        {
            splashBudget -= 1f;
            Vector2 random = Random.insideUnitCircle * effectRadius;
            Vector3 origin = new(camera.x + random.x, rainHeight, camera.z + random.y + 5f);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rainHeight + 20f, groundMask)) continue;
            HeroSkillBurst splash = HeroSkillBurst.Spawn(splashGraphPrefab, hit.point + Vector3.up * .035f);
            if (splash != null) splashCountThisSecond++;
        }
    }
}
