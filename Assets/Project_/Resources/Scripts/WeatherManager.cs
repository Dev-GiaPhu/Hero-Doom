using UnityEngine;
using UnityEngine.Serialization;

public enum WeatherType
{
    Sunny,
    Rain,
    Fog
}

public class WeatherManager : MonoBehaviour
{
    [Header("Weather Cycle")]
    public WeatherType startWeather = WeatherType.Sunny;
    [Min(5f)] public float minWeatherDuration = 45f;
    [Min(5f)] public float maxWeatherDuration = 90f;
    [Min(0.1f)] public float transitionDuration = 4f;
    public bool avoidImmediateRepeat = true;

    [Header("Random Weight")]
    [Min(0f)] public float sunnyWeight = 1f;
    [Min(0f)] public float rainWeight = 1f;
    [Min(0f)] public float fogWeight = 1f;

    [Header("Cloud Shadow Coverage")]
    public ProceduralCloudCookie cloudShadows;
    [FormerlySerializedAs("sunnyCloudDarkness")]
    [Range(0f, 1f)] public float sunnyCloudCoverage = 0.08f;
    [FormerlySerializedAs("rainCloudDarkness")]
    [Range(0f, 1f)] public float rainCloudCoverage = 0.48f;
    [FormerlySerializedAs("fogCloudDarkness")]
    [Range(0f, 1f)] public float fogCloudCoverage = 0.22f;

    [Header("Weather Light")]
    [Range(0.2f, 1f)] public float sunnyLightMultiplier = 1f;
    [Range(0.2f, 1f)] public float rainLightMultiplier = 0.52f;
    [Range(0.2f, 1f)] public float fogLightMultiplier = 0.72f;

    [Header("Weather Ambient")]
    [Range(0.2f, 1f)] public float sunnyAmbientMultiplier = 1f;
    [Range(0.2f, 1f)] public float rainAmbientMultiplier = 0.62f;
    [Range(0.2f, 1f)] public float fogAmbientMultiplier = 0.78f;

    public WeatherType CurrentWeather { get; private set; }
    public float RemainingTime => weatherTimer;

    private DayNightCycle3D dayNight;
    private float weatherTimer;
    private float transitionTimer;

    private float cloudFrom;
    private float cloudTo;
    private float lightFrom = 1f;
    private float lightTo = 1f;
    private float currentLight = 1f;

    private float baseAmbientIntensity = 1f;
    private float ambientFrom = 1f;
    private float ambientTo = 1f;
    private float currentAmbient = 1f;

    void Start()
    {
        if (cloudShadows == null)
            cloudShadows = GetComponent<ProceduralCloudCookie>();

        dayNight = GetComponent<DayNightCycle3D>();
        if (dayNight == null)
            dayNight = FindFirstObjectByType<DayNightCycle3D>();

        baseAmbientIntensity = RenderSettings.ambientIntensity;

        CurrentWeather = startWeather;

        cloudTo = GetCloudCoverage(CurrentWeather);
        cloudFrom = cloudTo;
        lightTo = GetLightMultiplier(CurrentWeather);
        lightFrom = lightTo;
        currentLight = lightTo;

        ambientTo = GetAmbientMultiplier(CurrentWeather);
        ambientFrom = ambientTo;
        currentAmbient = ambientTo;

        transitionTimer = transitionDuration;

        if (cloudShadows != null)
            cloudShadows.SetWeatherCoverage(cloudTo);

        if (dayNight != null)
            dayNight.SetWeatherIntensityMultiplier(currentLight);

        RenderSettings.ambientIntensity =
            baseAmbientIntensity * currentAmbient;

        ResetTimer();
    }

    void Update()
    {
        weatherTimer -= Time.deltaTime;

        if (weatherTimer <= 0f)
            ChangeWeather(PickNext());

        if (transitionTimer < transitionDuration)
        {
            transitionTimer += Time.deltaTime;

            float t = transitionDuration <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, transitionTimer / transitionDuration);

            if (cloudShadows != null)
                cloudShadows.SetWeatherCoverage(Mathf.Lerp(cloudFrom, cloudTo, t));

            currentLight = Mathf.Lerp(lightFrom, lightTo, t);
            currentAmbient = Mathf.Lerp(ambientFrom, ambientTo, t);

            if (dayNight != null)
                dayNight.SetWeatherIntensityMultiplier(currentLight);

            RenderSettings.ambientIntensity =
                baseAmbientIntensity * currentAmbient;
        }
    }

    public void ChangeWeather(WeatherType weather)
    {
        CurrentWeather = weather;

        cloudFrom = cloudShadows != null
            ? cloudShadows.WeatherCoverage
            : GetCloudCoverage(weather);

        cloudTo = GetCloudCoverage(weather);

        lightFrom = currentLight;
        lightTo = GetLightMultiplier(weather);

        ambientFrom = currentAmbient;
        ambientTo = GetAmbientMultiplier(weather);

        transitionTimer = 0f;
        ResetTimer();
    }

    private WeatherType PickNext()
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

        if (total <= 0f)
            return (WeatherType)(((int)CurrentWeather + 1) % 3);

        float roll = Random.value * total;

        if ((roll -= sunny) < 0f) return WeatherType.Sunny;
        if ((roll -= rain) < 0f) return WeatherType.Rain;
        return WeatherType.Fog;
    }

    private float GetCloudCoverage(WeatherType weather)
    {
        switch (weather)
        {
            case WeatherType.Rain: return rainCloudCoverage;
            case WeatherType.Fog: return fogCloudCoverage;
            default: return sunnyCloudCoverage;
        }
    }

    private float GetLightMultiplier(WeatherType weather)
    {
        switch (weather)
        {
            case WeatherType.Rain: return rainLightMultiplier;
            case WeatherType.Fog: return fogLightMultiplier;
            default: return sunnyLightMultiplier;
        }
    }

    private float GetAmbientMultiplier(WeatherType weather)
    {
        switch (weather)
        {
            case WeatherType.Rain: return rainAmbientMultiplier;
            case WeatherType.Fog: return fogAmbientMultiplier;
            default: return sunnyAmbientMultiplier;
        }
    }

    private void ResetTimer()
    {
        float min = Mathf.Max(5f, minWeatherDuration);
        float max = Mathf.Max(min, maxWeatherDuration);
        weatherTimer = Random.Range(min, max);
    }
}
