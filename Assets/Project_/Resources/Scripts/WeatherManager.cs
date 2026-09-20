using UnityEngine;

public enum WeatherType { Sunny, Rain, Fog }

public class WeatherManager : MonoBehaviour
{
    [Header("Weather Cycle")]
    public WeatherType startWeather = WeatherType.Sunny;
    [Min(1f)] public float minWeatherDuration = 45f;
    [Min(1f)] public float maxWeatherDuration = 90f;
    [Min(0.1f)] public float transitionDuration = 4f;
    public bool avoidImmediateRepeat = true;

    [Header("Random Weight")]
    [Min(0f)] public float sunnyWeight = 1f;
    [Min(0f)] public float rainWeight = 1f;
    [Min(0f)] public float fogWeight = 1f;

    [Header("Scene Effects")]
    public ParticleSystem rainParticles;
    public ParticleSystem groundRainSplashes;
    public ParticleSystem waterRainRipples;
    public ParticleSystem groundFogParticles;
    public ProceduralCloudCookie cloudShadows;

    [Header("Cloud By Weather")]
    [Range(0f, 1f)] public float sunnyCloudDarkness = 0.10f;
    [Range(0f, 1f)] public float rainCloudDarkness = 0.45f;
    [Range(0f, 1f)] public float fogCloudDarkness = 0.25f;

    public WeatherType CurrentWeather { get; private set; }
    public float RemainingTime => weatherTimer;

    private float weatherTimer;
    private float transitionTimer;
    private float cloudFrom;
    private float cloudTo;

    void Start()
    {
        CurrentWeather = startWeather;
        ApplyWeatherImmediate(CurrentWeather);
        ResetWeatherTimer();
    }

    void Update()
    {
        weatherTimer -= Time.deltaTime;
        if (weatherTimer <= 0f) ChangeWeather(PickNextWeather());

        if (transitionTimer < transitionDuration)
        {
            transitionTimer += Time.deltaTime;
            float t = transitionDuration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, transitionTimer / transitionDuration);
            if (cloudShadows != null) cloudShadows.SetCloudDarkness(Mathf.Lerp(cloudFrom, cloudTo, t));
        }
    }

    public void ChangeWeather(WeatherType next)
    {
        CurrentWeather = next;
        transitionTimer = 0f;
        cloudFrom = cloudShadows != null ? cloudShadows.cloudDarkness : 0f;
        cloudTo = GetCloudDarkness(next);
        SetParticleEmission(rainParticles, next == WeatherType.Rain);
        SetParticleEmission(groundRainSplashes, next == WeatherType.Rain);
        SetParticleEmission(waterRainRipples, next == WeatherType.Rain);
        SetParticleEmission(groundFogParticles, next == WeatherType.Fog);
        ResetWeatherTimer();
    }

    private void ApplyWeatherImmediate(WeatherType weather)
    {
        SetParticleEmission(rainParticles, weather == WeatherType.Rain);
        SetParticleEmission(groundRainSplashes, weather == WeatherType.Rain);
        SetParticleEmission(waterRainRipples, weather == WeatherType.Rain);
        SetParticleEmission(groundFogParticles, weather == WeatherType.Fog);
        cloudTo = GetCloudDarkness(weather);
        cloudFrom = cloudTo;
        transitionTimer = transitionDuration;
        if (cloudShadows != null) cloudShadows.SetCloudDarkness(cloudTo);
    }

    private WeatherType PickNextWeather()
    {
        float sunny = Mathf.Max(0f, sunnyWeight);
        float rain = Mathf.Max(0f, rainWeight);
        float fog = Mathf.Max(0f, fogWeight);
        if (avoidImmediateRepeat)
        {
            if (CurrentWeather == WeatherType.Sunny) sunny = 0f;
            if (CurrentWeather == WeatherType.Rain) rain = 0f;
            if (CurrentWeather == WeatherType.Fog) fog = 0f;
        }

        float total = sunny + rain + fog;
        if (total <= 0f) return (WeatherType)(((int)CurrentWeather + 1) % 3);
        float roll = Random.value * total;
        if ((roll -= sunny) < 0f) return WeatherType.Sunny;
        if ((roll -= rain) < 0f) return WeatherType.Rain;
        return WeatherType.Fog;
    }

    private float GetCloudDarkness(WeatherType weather)
    {
        switch (weather)
        {
            case WeatherType.Rain: return rainCloudDarkness;
            case WeatherType.Fog: return fogCloudDarkness;
            default: return sunnyCloudDarkness;
        }
    }

    private void ResetWeatherTimer()
    {
        float min = Mathf.Max(1f, minWeatherDuration);
        weatherTimer = Random.Range(min, Mathf.Max(min, maxWeatherDuration));
    }

    private static void SetParticleEmission(ParticleSystem ps, bool enabled)
    {
        if (ps == null) return;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = enabled;
        if (enabled && !ps.isPlaying) ps.Play();
        if (!enabled && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
