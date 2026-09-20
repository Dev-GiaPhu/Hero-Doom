using UnityEngine;

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

    [Header("Cloud By Weather")]
    public ProceduralCloudCookie cloudShadows;
    [Range(0f, 1f)] public float sunnyCloudDarkness = 0.08f;
    [Range(0f, 1f)] public float rainCloudDarkness = 0.48f;
    [Range(0f, 1f)] public float fogCloudDarkness = 0.22f;

    public WeatherType CurrentWeather { get; private set; }
    public float RemainingTime => weatherTimer;

    private float weatherTimer;
    private float transitionTimer;
    private float cloudFrom;
    private float cloudTo;

    void Start()
    {
        if (cloudShadows == null) cloudShadows = GetComponent<ProceduralCloudCookie>();
        CurrentWeather = startWeather;
        cloudTo = GetCloudDarkness(CurrentWeather);
        cloudFrom = cloudTo;
        transitionTimer = transitionDuration;
        if (cloudShadows != null) cloudShadows.SetCloudDarkness(cloudTo);
        ResetTimer();
    }

    void Update()
    {
        weatherTimer -= Time.deltaTime;
        if (weatherTimer <= 0f) ChangeWeather(PickNext());

        if (transitionTimer < transitionDuration)
        {
            transitionTimer += Time.deltaTime;
            float t = transitionDuration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, transitionTimer / transitionDuration);
            if (cloudShadows != null) cloudShadows.SetCloudDarkness(Mathf.Lerp(cloudFrom, cloudTo, t));
        }
    }

    public void ChangeWeather(WeatherType weather)
    {
        CurrentWeather = weather;
        cloudFrom = cloudShadows != null ? cloudShadows.cloudDarkness : GetCloudDarkness(weather);
        cloudTo = GetCloudDarkness(weather);
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

    private void ResetTimer()
    {
        float min = Mathf.Max(5f, minWeatherDuration);
        float max = Mathf.Max(min, maxWeatherDuration);
        weatherTimer = Random.Range(min, max);
    }
}
