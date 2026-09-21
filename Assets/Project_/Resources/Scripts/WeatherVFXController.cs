using UnityEngine;

public class WeatherVFXController : MonoBehaviour
{
    [Header("Scene References")]
    public WeatherManager weather;
    public Camera targetCamera;

    [Header("Rain")]
    [Min(0.1f)] public float weatherFadeSpeed = 0.65f;
    public float rainHeight = 16f;
    public float effectRadius = 22f;
    public LayerMask groundMask;

    [Header("Rain Visual")]
    [Min(0f)] public float rainEmissionRate = 420f;
    public Color rainColor = new Color(0.34f, 0.46f, 0.58f, 0.34f);
    public Vector2 rainSizeRange = new Vector2(0.022f, 0.042f);
    public Vector2 rainFallSpeedRange = new Vector2(18f, 24f);
    [Range(0.5f, 8f)] public float rainStreakLength = 3.8f;
    [Range(0.005f, 0.15f)] public float rainVelocityScale = 0.04f;

    [Header("Ground Splash")]
    [Min(0f)] public float splashRate = 13f;

    private ParticleSystem rain;
    private ParticleSystem splashes;

    private Material rainMaterial;
    private Material splashMaterial;

    private float rainIntensity;
    private float splashBudget;

    void Start()
    {
        if (groundMask.value == 0)
        {
            int layer = LayerMask.NameToLayer("Ground");
            if (layer >= 0)
                groundMask = 1 << layer;
        }

        BuildRainVFX();
    }

    void Update()
    {
        if (weather == null || targetCamera == null)
            return;

        float rainTarget =
            weather.CurrentWeather == WeatherType.Rain
                ? 1f
                : 0f;

        rainIntensity =
            Mathf.MoveTowards(
                rainIntensity,
                rainTarget,
                weatherFadeSpeed * Time.deltaTime);

        Vector3 cam = targetCamera.transform.position;

        if (rain != null)
        {
            rain.transform.position =
                new Vector3(
                    cam.x,
                    rainHeight,
                    cam.z + 4f);
        }

        SetEmission(rain, rainEmissionRate * rainIntensity);

        Shader.SetGlobalFloat(
            "_HeroDoomRainIntensity",
            rainIntensity);

        EmitGroundSplashes(cam);
    }

    private void BuildRainVFX()
    {
        rainMaterial =
            CreateTransparentParticleMaterial(
                CreateStreakTexture(),
                rainColor);

        splashMaterial =
            CreateTransparentParticleMaterial(
                CreateRingTexture(64),
                new Color(0.58f, 0.76f, 0.86f, 0.28f));

        BuildRain();
        BuildSplashes();
    }

    private void BuildRain()
    {
        rain = CreateSystem("Weather_Rain");

        var main = rain.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.05f, 1.35f);
        main.startSpeed = 0f;
        main.startSize =
            new ParticleSystem.MinMaxCurve(
                Mathf.Min(rainSizeRange.x, rainSizeRange.y),
                Mathf.Max(rainSizeRange.x, rainSizeRange.y));
        main.maxParticles = 1600;
        main.startColor = new Color(1f, 1f, 1f, 0.85f);

        var shape = rain.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(effectRadius * 2f, 0.5f, effectRadius * 2f);

        var velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;

        // Unity yêu cầu X/Y/Z của VelocityOverLifetime dùng cùng một curve mode.
        // Set cả 3 về TwoConstants để tránh:
        // "Particle Velocity curves must all be in the same mode".
        float minFall = Mathf.Min(rainFallSpeedRange.x, rainFallSpeedRange.y);
        float maxFall = Mathf.Max(rainFallSpeedRange.x, rainFallSpeedRange.y);
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(-maxFall, -minFall);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var renderer = rain.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = rainStreakLength;
        renderer.velocityScale = rainVelocityScale;
        renderer.material = rainMaterial;
    }

    private void BuildSplashes()
    {
        splashes = CreateSystem("Weather_GroundSplashes");

        var main = splashes.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.58f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.58f);
        main.maxParticles = 300;

        var emission = splashes.emission;
        emission.rateOverTime = 0f;

        var sizeOverLifetime = splashes.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size =
            new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 0.28f, 1f, 1.75f));

        var colorOverLifetime = splashes.colorOverLifetime;
        colorOverLifetime.enabled = true;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.45f, 0f),
                new GradientAlphaKey(0f, 1f)
            });

        colorOverLifetime.color = gradient;

        var renderer = splashes.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.material = splashMaterial;
    }

    private ParticleSystem CreateSystem(string systemName)
    {
        GameObject go = new GameObject(systemName);
        go.transform.SetParent(transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        ps.Play();

        return ps;
    }

    private void EmitGroundSplashes(Vector3 cam)
    {
        if (splashes == null || rainIntensity <= 0.01f)
            return;

        splashBudget += rainIntensity * splashRate * Time.deltaTime;

        int emitCount =
            Mathf.Min(3, Mathf.FloorToInt(splashBudget));

        splashBudget -= emitCount;

        for (int i = 0; i < emitCount; i++)
        {
            Vector2 random = Random.insideUnitCircle * effectRadius;

            Vector3 origin =
                new Vector3(
                    cam.x + random.x,
                    15f,
                    cam.z + random.y + 5f);

            if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit hit,
                    40f,
                    groundMask))
                continue;

            ParticleSystem.EmitParams ep =
                new ParticleSystem.EmitParams();

            ep.position = hit.point + Vector3.up * 0.035f;
            ep.startSize = Random.Range(0.24f, 0.58f);
            ep.startLifetime = Random.Range(0.32f, 0.58f);

            splashes.Emit(ep, 1);
        }
    }

    private static void SetEmission(ParticleSystem ps, float rate)
    {
        if (ps == null) return;

        var emission = ps.emission;
        emission.rateOverTime = rate;
    }

    private Material CreateTransparentParticleMaterial(
        Texture2D texture,
        Color color)
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);
        material.name = "RuntimeRainMaterial";
        material.color = color;
        material.renderQueue = 3000;

        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);

        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", texture);

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);

        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", 5f);

        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", 10f);

        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
    }

    private static Texture2D CreateStreakTexture()
    {
        Texture2D tex =
            new Texture2D(
                2,
                20,
                TextureFormat.RGBA32,
                false);

        for (int y = 0; y < 20; y++)
        {
            float t = y / 19f;
            float alpha = Mathf.Sin(t * Mathf.PI);
            alpha *= alpha;

            Color color =
                new Color(1f, 1f, 1f, alpha);

            tex.SetPixel(0, y, color);
            tex.SetPixel(1, y, color);
        }

        tex.Apply();
        return tex;
    }

    private static Texture2D CreateRingTexture(int size)
    {
        Texture2D tex =
            new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 p =
                new Vector2(
                    x / (float)(size - 1),
                    y / (float)(size - 1))
                * 2f
                - Vector2.one;

            float distance = p.magnitude;

            float alpha =
                Mathf.Clamp01(
                    1f -
                    Mathf.Abs(distance - 0.72f) / 0.10f)
                * Mathf.Clamp01(1f - distance);

            tex.SetPixel(
                x,
                y,
                new Color(1f, 1f, 1f, alpha));
        }

        tex.Apply();
        return tex;
    }
}
