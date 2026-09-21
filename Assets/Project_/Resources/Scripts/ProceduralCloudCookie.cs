using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Light))]
public class ProceduralCloudCookie : MonoBehaviour
{
    [Header("Cloud Texture")]
    [Range(64, 512)]
    public int resolution = 256;

    [Tooltip("Kích thước khối mây. Giá trị lớn tạo mảng mây lớn hơn, ít vụn hơn.")]
    [Range(0.5f, 6f)]
    public float cloudSize = 2.8f;

    [Tooltip("Mật độ tổng thể của mây trước khi Weather thêm/bớt cloud coverage.")]
    [Range(0f, 1f)]
    public float cloudDensity = 0.48f;

    [Tooltip("Độ đậm tối đa của bóng mây trên mặt đất.")]
    [Range(0f, 1f)]
    public float cloudDarkness = 0.58f;

    [Tooltip("Độ mềm của rìa bóng mây.")]
    [Range(0.02f, 0.35f)]
    public float edgeSoftness = 0.11f;

    [Header("Cloud Edge Detail")]
    [Tooltip("Tần số noise nhỏ ở rìa mây. Cao hơn = mép có nhiều chi tiết nhỏ hơn.")]
    [Range(1f, 12f)]
    public float edgeDetailScale = 5.5f;

    [Tooltip("Mức độ noise nhỏ làm biến dạng rìa khối mây.")]
    [Range(0f, 0.6f)]
    public float edgeDetailStrength = 0.24f;

    [Tooltip("Noise rất nhỏ bổ sung để tránh rìa mây quá trơn.")]
    [Range(2f, 24f)]
    public float microDetailScale = 11f;

    [Range(0f, 0.25f)]
    public float microDetailStrength = 0.07f;

    [Header("Slow Shape Morph")]
    [Tooltip("Tốc độ hình dạng mây từ từ biến đổi, độc lập với tốc độ mây trôi.")]
    [Range(0f, 0.2f)]
    public float morphSpeed = 0.025f;

    [Tooltip("Độ mạnh của biến đổi hình dạng theo thời gian.")]
    [Range(0f, 1f)]
    public float morphStrength = 0.42f;

    [Tooltip("Khoảng thời gian cập nhật noise động. Cao hơn nhẹ CPU hơn nhưng morph kém mượt hơn.")]
    [Range(0.1f, 1f)]
    public float textureUpdateInterval = 0.35f;

    [Header("Projected Shadow Size")]
    [Tooltip("Kích thước vùng cookie trên mặt đất. Tăng giá trị này để bóng mây thành các khối lớn hơn trong world.")]
    public Vector2 cookieSize = new Vector2(120f, 120f);

    [Header("Wind")]
    [Tooltip("Tốc độ bóng mây trôi trên mặt đất.")]
    public float scrollSpeedX = 0.015f;
    public float scrollSpeedY = 0.007f;

    public float WeatherCoverage { get; private set; } = 0.25f;

    private Light directionalLight;
    private UniversalAdditionalLightData urpLightData;
    private Texture2D cloudTexture;
    private Color32[] pixelBuffer;
    private Vector2 currentOffset;
    private float morphTime;
    private float textureTimer;
    private bool cookieDirty = true;

    void Start()
    {
        directionalLight = GetComponent<Light>();
        urpLightData = GetComponent<UniversalAdditionalLightData>();

        EnsureTexture();
        ApplyCookieSize();
        UpdateCookieTexture();
    }

    void Update()
    {
        if (urpLightData == null)
            return;

        currentOffset.x += scrollSpeedX * Time.deltaTime;
        currentOffset.y += scrollSpeedY * Time.deltaTime;
        urpLightData.lightCookieOffset = currentOffset;

        morphTime += Time.deltaTime * morphSpeed;
        textureTimer -= Time.deltaTime;

        if (textureTimer <= 0f)
        {
            textureTimer = Mathf.Max(0.1f, textureUpdateInterval);

            if (morphSpeed > 0f || cookieDirty)
                UpdateCookieTexture();
        }
    }

    public void SetCloudDarkness(float darkness)
    {
        darkness = Mathf.Clamp01(darkness);

        if (Mathf.Abs(cloudDarkness - darkness) < 0.005f)
            return;

        cloudDarkness = darkness;
        cookieDirty = true;
    }

    public void SetWeatherCoverage(float coverage)
    {
        coverage = Mathf.Clamp01(coverage);

        if (Mathf.Abs(WeatherCoverage - coverage) < 0.005f)
            return;

        WeatherCoverage = coverage;
        cookieDirty = true;
    }

    private void EnsureTexture()
    {
        int size = Mathf.Clamp(resolution, 64, 512);

        if (cloudTexture != null &&
            cloudTexture.width == size &&
            cloudTexture.height == size)
        {
            return;
        }

        if (cloudTexture != null)
            Destroy(cloudTexture);

        cloudTexture =
            new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true);

        cloudTexture.name = "Runtime_LargeCloudShadowCookie";
        cloudTexture.wrapMode = TextureWrapMode.Repeat;
        cloudTexture.filterMode = FilterMode.Bilinear;

        pixelBuffer = new Color32[size * size];

        if (directionalLight == null)
            directionalLight = GetComponent<Light>();

        directionalLight.cookie = cloudTexture;
        cookieDirty = true;
    }

    private void ApplyCookieSize()
    {
        if (urpLightData != null)
            urpLightData.lightCookieSize = cookieSize;
    }

    private void UpdateCookieTexture()
    {
        EnsureTexture();
        ApplyCookieSize();

        int size = cloudTexture.width;
        float shapeFrequency = 1.85f / Mathf.Max(0.5f, cloudSize);

        Vector2 morphA =
            new Vector2(
                Mathf.Sin(morphTime * 0.83f),
                Mathf.Cos(morphTime * 0.67f)) *
            morphStrength * 0.42f;

        Vector2 morphB =
            new Vector2(
                Mathf.Cos(morphTime * 0.51f + 1.7f),
                Mathf.Sin(morphTime * 0.74f + 0.9f)) *
            morphStrength * 0.31f;

        float effectiveDensity =
            Mathf.Clamp01(
                cloudDensity +
                (WeatherCoverage - 0.25f) * 0.72f);

        float threshold =
            Mathf.Lerp(
                0.72f,
                0.36f,
                effectiveDensity);

        for (int y = 0; y < size; y++)
        {
            float v = y / (float)size;

            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;

                float largeA =
                    SeamlessNoise(
                        u + morphA.x,
                        v + morphA.y,
                        shapeFrequency);

                float largeB =
                    SeamlessNoise(
                        u + 0.371f + morphB.x,
                        v + 0.193f + morphB.y,
                        shapeFrequency * 1.63f);

                float largeShape =
                    Mathf.Lerp(
                        largeA,
                        largeA * 0.72f + largeB * 0.28f,
                        morphStrength);

                float detail =
                    SeamlessNoise(
                        u + 0.117f - morphB.y * 0.3f,
                        v + 0.419f + morphA.x * 0.3f,
                        shapeFrequency * edgeDetailScale);

                float micro =
                    SeamlessNoise(
                        u + 0.613f,
                        v + 0.271f,
                        shapeFrequency * microDetailScale);

                float field =
                    largeShape +
                    (detail - 0.5f) * edgeDetailStrength +
                    (micro - 0.5f) * microDetailStrength;

                float cloud =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            threshold - edgeSoftness,
                            threshold + edgeSoftness,
                            field));

                float shadow =
                    Mathf.Lerp(
                        1f,
                        1f - cloudDarkness,
                        cloud);

                byte gray =
                    (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(shadow) * 255f);

                pixelBuffer[y * size + x] =
                    new Color32(gray, gray, gray, 255);
            }
        }

        cloudTexture.SetPixels32(pixelBuffer);
        cloudTexture.Apply(false, false);

        directionalLight.cookie = cloudTexture;
        cookieDirty = false;
    }

    private static float SeamlessNoise(
        float u,
        float v,
        float frequency)
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

    void OnValidate()
    {
        resolution = Mathf.Clamp(resolution, 64, 512);
        cloudSize = Mathf.Clamp(cloudSize, 0.5f, 6f);
        cloudDensity = Mathf.Clamp01(cloudDensity);
        cloudDarkness = Mathf.Clamp01(cloudDarkness);

        if (Application.isPlaying)
        {
            cookieDirty = true;
            ApplyCookieSize();
        }
    }

    void OnDestroy()
    {
        if (cloudTexture != null)
            Destroy(cloudTexture);
    }
}
