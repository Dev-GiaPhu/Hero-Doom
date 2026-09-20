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

    private Vector2 currentOffset;

    void Start()
    {
        directionalLight = GetComponent<Light>();
        urpLightData = GetComponent<UniversalAdditionalLightData>();
        RebuildCookie();
        if (urpLightData != null) urpLightData.lightCookieSize = cookieSize;
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
        if (Mathf.Abs(cloudDarkness - darkness) < 0.025f) return;
        cloudDarkness = darkness;
        RebuildCookie();
    }

    private void RebuildCookie()
    {
        if (directionalLight == null) directionalLight = GetComponent<Light>();
        if (cloudTexture != null) Destroy(cloudTexture);
        cloudTexture = GenerateSeamlessCloudCookie();
        directionalLight.cookie = cloudTexture;
    }

    private Texture2D GenerateSeamlessCloudCookie()
    {
        int size = Mathf.Max(8, resolution);
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Runtime_SeamlessCloudCookie";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float n00 = Mathf.PerlinNoise(u * scale, v * scale);
                float n10 = Mathf.PerlinNoise((u - 1f) * scale, v * scale);
                float n01 = Mathf.PerlinNoise(u * scale, (v - 1f) * scale);
                float n11 = Mathf.PerlinNoise((u - 1f) * scale, (v - 1f) * scale);
                float noise = Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
                byte gray = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, 255f * (1f - cloudDarkness), noise));
                pixels[y * size + x] = new Color32(gray, gray, gray, 255);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    void OnDestroy()
    {
        if (cloudTexture != null) Destroy(cloudTexture);
    }
}
