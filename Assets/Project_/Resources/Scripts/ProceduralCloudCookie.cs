using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Light))]
public class ProceduralCloudCookie : MonoBehaviour
{
    private Light directionalLight;
    private UniversalAdditionalLightData urpLightData;
    private Texture2D cloudTexture;

    [Header("Cloud Shadow")]
    [Range(64, 512)] public int resolution = 256;
    [Min(0.1f)] public float scale = 5f;
    [Range(0f, 1f)] public float cloudDarkness = 0.3f;
    public Vector2 cookieSize = new Vector2(50f, 50f);

    [Header("Wind")]
    public float scrollSpeedX = 0.05f;
    public float scrollSpeedY = 0.02f;

    public float WeatherCoverage { get; private set; } = 0.25f;

    private Vector2 currentOffset;

    void Start()
    {
        directionalLight = GetComponent<Light>();
        urpLightData = GetComponent<UniversalAdditionalLightData>();

        RebuildCookie();

        if (urpLightData != null)
            urpLightData.lightCookieSize = cookieSize;
    }

    void Update()
    {
        if (urpLightData == null) return;

        currentOffset.x = Mathf.Repeat(currentOffset.x + scrollSpeedX * Time.deltaTime, 1f);
        currentOffset.y = Mathf.Repeat(currentOffset.y + scrollSpeedY * Time.deltaTime, 1f);
        urpLightData.lightCookieOffset = currentOffset;
    }

    public void SetCloudDarkness(float darkness)
    {
        darkness = Mathf.Clamp01(darkness);
        if (Mathf.Abs(cloudDarkness - darkness) < 0.02f) return;

        cloudDarkness = darkness;
        RebuildCookie();
    }

    public void SetWeatherCoverage(float coverage)
    {
        coverage = Mathf.Clamp01(coverage);
        if (Mathf.Abs(WeatherCoverage - coverage) < 0.025f) return;

        WeatherCoverage = coverage;
        RebuildCookie();
    }

    private void RebuildCookie()
    {
        if (directionalLight == null)
            directionalLight = GetComponent<Light>();

        if (cloudTexture != null)
            Destroy(cloudTexture);

        cloudTexture = GenerateCloudCookie();
        directionalLight.cookie = cloudTexture;
    }

    private Texture2D GenerateCloudCookie()
    {
        int size = Mathf.Max(8, resolution);

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Runtime_CloudShadowCookie";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;

            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;

                float n1 = SeamlessNoise(u, v, scale);
                float n2 = SeamlessNoise(u + 0.173f, v + 0.319f, scale * 2.07f);
                float n3 = SeamlessNoise(u + 0.441f, v + 0.127f, scale * 0.53f);
                float noise = n1 * 0.58f + n2 * 0.27f + n3 * 0.15f;

                // Coverage điều khiển số lượng mảng mây, cloudDarkness vẫn giữ đúng strength
                // mà người dùng đã tune trong Inspector.
                float threshold = Mathf.Lerp(0.69f, 0.43f, WeatherCoverage);
                float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(threshold, threshold + 0.12f, noise));

                byte gray = (byte)Mathf.RoundToInt(
                    Mathf.Lerp(255f, 255f * (1f - cloudDarkness), cloud));

                pixels[y * size + x] = new Color32(gray, gray, gray, 255);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private float SeamlessNoise(float u, float v, float frequency)
    {
        u = Mathf.Repeat(u, 1f);
        v = Mathf.Repeat(v, 1f);

        float n00 = Mathf.PerlinNoise(u * frequency, v * frequency);
        float n10 = Mathf.PerlinNoise((u - 1f) * frequency, v * frequency);
        float n01 = Mathf.PerlinNoise(u * frequency, (v - 1f) * frequency);
        float n11 = Mathf.PerlinNoise((u - 1f) * frequency, (v - 1f) * frequency);

        float nx0 = Mathf.Lerp(n00, n10, u);
        float nx1 = Mathf.Lerp(n01, n11, u);
        return Mathf.Lerp(nx0, nx1, v);
    }

    void OnDestroy()
    {
        if (cloudTexture != null)
            Destroy(cloudTexture);
    }
}
