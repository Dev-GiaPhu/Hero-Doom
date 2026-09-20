using UnityEngine;

public class DayNightCycle3D : MonoBehaviour
{
    [Header("Time")]
    [Min(10f)] public float dayDuration = 120f;
    [Range(0f, 1f)] public float startTime = 0.25f;

    [Header("Sun")]
    public Light directionalLight;
    public Vector2 sunAngleRange = new Vector2(-90f, 270f);
    public float sunYaw = 30f;
    public Gradient sunColor;
    public AnimationCurve sunIntensity;

    public float TimeFraction { get; private set; }

    void Awake()
    {
        TimeFraction = Mathf.Repeat(startTime, 1f);
        ApplyLighting();
    }

    void Update()
    {
        if (directionalLight == null || dayDuration <= 0f) return;
        TimeFraction = Mathf.Repeat(TimeFraction + Time.deltaTime / dayDuration, 1f);
        ApplyLighting();
    }

    private void ApplyLighting()
    {
        if (directionalLight == null) return;
        float sunAngle = Mathf.Lerp(sunAngleRange.x, sunAngleRange.y, TimeFraction);
        directionalLight.transform.rotation = Quaternion.Euler(sunAngle, sunYaw, 0f);
        directionalLight.color = sunColor.Evaluate(TimeFraction);
        directionalLight.intensity = Mathf.Max(0f, sunIntensity.Evaluate(TimeFraction));
    }
}
