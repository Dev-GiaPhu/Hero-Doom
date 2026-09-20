using UnityEngine;

public class WeatherVFXController : MonoBehaviour
{
    public WeatherManager weather;
    public Camera targetCamera;
    [Min(0.1f)] public float weatherFadeSpeed = 0.65f;
    public float rainHeight = 16f;
    public float effectRadius = 22f;
    public LayerMask groundMask;

    private ParticleSystem rain;
    private ParticleSystem fog;
    private ParticleSystem splashes;
    private Material rainMaterial;
    private Material fogMaterial;
    private Material splashMaterial;
    private float rainIntensity;
    private float fogIntensity;
    private float splashBudget;

    void Start()
    {
        if (weather == null) weather = FindFirstObjectByType<WeatherManager>();
        if (targetCamera == null) targetCamera = Camera.main;
        if (groundMask.value == 0)
        {
            int layer = LayerMask.NameToLayer("Ground");
            if (layer >= 0) groundMask = 1 << layer;
        }

        BuildVFX();
    }

    void Update()
    {
        if (weather == null || targetCamera == null) return;

        float rainTarget = weather.CurrentWeather == WeatherType.Rain ? 1f : 0f;
        float fogTarget = weather.CurrentWeather == WeatherType.Fog ? 1f : 0f;

        rainIntensity = Mathf.MoveTowards(rainIntensity, rainTarget, weatherFadeSpeed * Time.deltaTime);
        fogIntensity = Mathf.MoveTowards(fogIntensity, fogTarget, weatherFadeSpeed * Time.deltaTime);

        Vector3 cam = targetCamera.transform.position;
        if (rain != null) rain.transform.position = new Vector3(cam.x, rainHeight, cam.z + 4f);
        if (fog != null) fog.transform.position = new Vector3(cam.x, 1.8f, cam.z + 5f);

        SetEmission(rain, 950f * rainIntensity);
        SetEmission(fog, 16f * fogIntensity);
        Shader.SetGlobalFloat("_HeroDoomRainIntensity", rainIntensity);

        EmitGroundSplashes(cam);
    }

    private void BuildVFX()
    {
        rainMaterial = CreateParticleMaterial(CreateStreakTexture(), new Color(0.75f, 0.86f, 1f, 0.72f));
        fogMaterial = CreateParticleMaterial(CreateSoftBlobTexture(64), new Color(0.78f, 0.84f, 0.86f, 0.16f));
        splashMaterial = CreateParticleMaterial(CreateRingTexture(64), new Color(0.76f, 0.9f, 1f, 0.52f));

        rain = CreateSystem("Weather_Rain");
        var rainMain = rain.main;
        rainMain.simulationSpace = ParticleSystemSimulationSpace.World;
        rainMain.startLifetime = 1.45f;
        rainMain.startSpeed = 0f;
        rainMain.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.07f);
        rainMain.maxParticles = 3500;
        rainMain.startColor = Color.white;

        var rainShape = rain.shape;
        rainShape.enabled = true;
        rainShape.shapeType = ParticleSystemShapeType.Box;
        rainShape.scale = new Vector3(effectRadius * 2f, 1f, effectRadius * 2f);

        var rainVel = rain.velocityOverLifetime;
        rainVel.enabled = true;
        rainVel.y = -18f;

        var rainRenderer = rain.GetComponent<ParticleSystemRenderer>();
        rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        rainRenderer.lengthScale = 8f;
        rainRenderer.velocityScale = 0.08f;
        rainRenderer.material = rainMaterial;

        fog = CreateSystem("Weather_GroundFog");
        var fogMain = fog.main;
        fogMain.simulationSpace = ParticleSystemSimulationSpace.World;
        fogMain.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
        fogMain.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        fogMain.startSize = new ParticleSystem.MinMaxCurve(6f, 12f);
        fogMain.maxParticles = 260;
        fogMain.startColor = new Color(1f, 1f, 1f, 0.45f);

        var fogShape = fog.shape;
        fogShape.enabled = true;
        fogShape.shapeType = ParticleSystemShapeType.Box;
        fogShape.scale = new Vector3(effectRadius * 2f, 2.4f, effectRadius * 2f);

        var fogNoise = fog.noise;
        fogNoise.enabled = true;
        fogNoise.strength = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
        fogNoise.frequency = 0.08f;
        fogNoise.scrollSpeed = 0.12f;
        fogNoise.damping = true;

        var fogRenderer = fog.GetComponent<ParticleSystemRenderer>();
        fogRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        fogRenderer.material = fogMaterial;

        splashes = CreateSystem("Weather_GroundSplashes");
        var splashMain = splashes.main;
        splashMain.simulationSpace = ParticleSystemSimulationSpace.World;
        splashMain.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        splashMain.startSpeed = 0f;
        splashMain.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        splashMain.maxParticles = 600;

        var splashEmission = splashes.emission;
        splashEmission.rateOverTime = 0f;

        var splashSize = splashes.sizeOverLifetime;
        splashSize.enabled = true;
        splashSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1.8f));

        var splashColor = splashes.colorOverLifetime;
        splashColor.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
        splashColor.color = gradient;

        var splashRenderer = splashes.GetComponent<ParticleSystemRenderer>();
        splashRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        splashRenderer.material = splashMaterial;
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
        if (splashes == null || rainIntensity <= 0.01f) return;

        splashBudget += rainIntensity * 30f * Time.deltaTime;
        int emitCount = Mathf.Min(6, Mathf.FloorToInt(splashBudget));
        splashBudget -= emitCount;

        for (int i = 0; i < emitCount; i++)
        {
            Vector2 r = Random.insideUnitCircle * effectRadius;
            Vector3 origin = new Vector3(cam.x + r.x, 15f, cam.z + r.y + 5f);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 40f, groundMask)) continue;

            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            ep.position = hit.point + Vector3.up * 0.035f;
            ep.startSize = Random.Range(0.3f, 0.75f);
            ep.startLifetime = Random.Range(0.35f, 0.7f);
            splashes.Emit(ep, 1);
        }
    }

    private static void SetEmission(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var emission = ps.emission;
        emission.rateOverTime = rate;
    }

    private Material CreateParticleMaterial(Texture2D texture, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        mat.name = "RuntimeWeatherMaterial";
        mat.color = color;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
        else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
        return mat;
    }

    private static Texture2D CreateStreakTexture()
    {
        Texture2D tex = new Texture2D(2, 16, TextureFormat.RGBA32, false);
        for (int y = 0; y < 16; y++)
        {
            float edge = Mathf.Sin((y / 15f) * Mathf.PI);
            Color c = new Color(1f, 1f, 1f, edge);
            tex.SetPixel(0, y, c);
            tex.SetPixel(1, y, c);
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateSoftBlobTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 p = new Vector2(x / (float)(size - 1), y / (float)(size - 1)) * 2f - Vector2.one;
            float alpha = Mathf.Pow(Mathf.Clamp01(1f - p.magnitude), 1.6f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateRingTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 p = new Vector2(x / (float)(size - 1), y / (float)(size - 1)) * 2f - Vector2.one;
            float d = p.magnitude;
            float alpha = Mathf.Clamp01(1f - Mathf.Abs(d - 0.72f) / 0.11f) * Mathf.Clamp01(1f - d);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        return tex;
    }
}
