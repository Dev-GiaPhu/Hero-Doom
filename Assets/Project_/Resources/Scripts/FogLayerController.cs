using UnityEngine;

[ExecuteAlways]
public class FogLayerController : MonoBehaviour
{
    [Header("Scene References")]
    public WeatherManager weather;
    public Camera targetCamera;
    public Renderer fogRenderer;
    public Material fogMaterial;

    [Header("Fog Noise Textures")]
    [Tooltip("Noise lớn tạo hình các mảng sương. Có thể kéo texture riêng vào đây.")]
    public Texture2D shapeNoiseMap;

    [Tooltip("Noise nhỏ dùng riêng cho chi tiết/rìa sương. Không dùng chung texture Shape để tránh pattern bị lệch hoặc lặp giả.")]
    public Texture2D detailNoiseMap;

    [Tooltip("Texture nào để trống thì script tự tạo đúng loại texture đó.")]
    public bool generateMissingNoise = true;

    [Header("Generated Noise")]
    [Range(64, 512)] public int generatedNoiseResolution = 256;
    public int generatedNoiseSeed = 1337;

    [Tooltip("Tần số khi tạo Shape Noise. Thấp = mảng lớn hơn.")]
    [Range(0.5f, 8f)] public float generatedShapeNoiseScale = 2.8f;

    [Tooltip("Tần số khi tạo Detail Noise.")]
    [Range(2f, 20f)] public float generatedDetailNoiseScale = 8f;

    [Header("Fog Coverage")]
    [Min(10f)] public float areaSize = 90f;
    [Min(0f)] public float heightAboveGround = 2.2f;
    [Min(0f)] public float cameraForwardOffset = 8f;
    public bool followCamera = true;

    [Header("Fog Shape")]
    [Tooltip("Kích thước trung bình của từng vùng sương trong world. Cao hơn = mảng sương lớn hơn.")]
    [Range(6f, 60f)] public float fogPatchSize = 24f;

    [Tooltip("Mật độ vùng có sương khi Weather = Fog.")]
    [Range(0f, 1f)] public float fogDensity = 0.48f;

    [Tooltip("Độ trong/đục tối đa của lớp sương.")]
    [Range(0f, 1f)] public float fogOpacity = 0.52f;

    [Tooltip("Độ mềm giữa vùng sương dày và vùng sương mỏng.")]
    [Range(0.01f, 0.45f)] public float edgeSoftness = 0.2f;

    [Tooltip("Độ tương phản density bên trong mảng sương.")]
    [Range(0.4f, 3f)] public float fogContrast = 1.25f;

    public Color fogColor = new Color(0.72f, 0.78f, 0.8f, 1f);

    [Header("Fog Edge Detail")]
    [Tooltip("Scale noise nhỏ ở mép. Cao hơn = chi tiết nhỏ hơn.")]
    [Range(1f, 8f)] public float edgeDetailScale = 2.4f;

    [Tooltip("Mức độ detail noise làm biến dạng các vùng sương.")]
    [Range(0f, 0.75f)] public float edgeDetailStrength = 0.28f;

    [Tooltip("Scale lớp detail rất nhỏ để tránh mép quá trơn.")]
    [Range(2f, 16f)] public float microDetailScale = 5.5f;

    [Range(0f, 0.3f)] public float microDetailStrength = 0.08f;

    [Header("Fog Movement")]
    [Tooltip("Tốc độ trôi của sương theo X/Z. Nên để rất chậm.")]
    public Vector2 windSpeed = new Vector2(0.006f, 0.0025f);

    [Tooltip("Tốc độ hình dạng sương biến đổi chậm theo thời gian.")]
    [Range(0f, 0.15f)] public float morphSpeed = 0.018f;

    [Tooltip("Mức độ hình dạng sương biến đổi.")]
    [Range(0f, 1f)] public float morphStrength = 0.32f;

    [Header("Transition")]
    [Min(0.05f)] public float fadeSpeed = 0.3f;

    [Header("Editor Preview")]
    public bool previewInEditMode = true;
    [Range(0f, 1f)] public float editModePreviewDensity = 0.36f;

    private static readonly int ShapeNoiseTex = Shader.PropertyToID("_ShapeNoiseTex");
    private static readonly int DetailNoiseTex = Shader.PropertyToID("_DetailNoiseTex");
    private static readonly int Density = Shader.PropertyToID("_Density");
    private static readonly int FogOpacity = Shader.PropertyToID("_FogOpacity");
    private static readonly int FogColor = Shader.PropertyToID("_FogColor");
    private static readonly int PatchSize = Shader.PropertyToID("_PatchSize");
    private static readonly int EdgeSoftness = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int FogContrast = Shader.PropertyToID("_FogContrast");
    private static readonly int EdgeDetailScale = Shader.PropertyToID("_EdgeDetailScale");
    private static readonly int EdgeDetailStrength = Shader.PropertyToID("_EdgeDetailStrength");
    private static readonly int MicroDetailScale = Shader.PropertyToID("_MicroDetailScale");
    private static readonly int MicroDetailStrength = Shader.PropertyToID("_MicroDetailStrength");
    private static readonly int WindSpeed = Shader.PropertyToID("_WindSpeed");
    private static readonly int MorphSpeed = Shader.PropertyToID("_MorphSpeed");
    private static readonly int MorphStrength = Shader.PropertyToID("_MorphStrength");

    private MaterialPropertyBlock propertyBlock;
    private Texture2D generatedShapeNoise;
    private Texture2D generatedDetailNoise;
    private float currentDensity;
    private int generatedSettingsHash;

    void Awake()
    {
        ResolveReferences();
        EnsureGeneratedTextures();
        ApplyAllProperties();
    }

    void OnEnable()
    {
        ResolveReferences();
        EnsureGeneratedTextures();
        ApplyAllProperties();
    }

    void Update()
    {
        ResolveReferences();
        EnsureGeneratedTextures();
        UpdateTransform();

        float targetDensity;

        if (!Application.isPlaying)
        {
            targetDensity =
                previewInEditMode
                    ? editModePreviewDensity
                    : 0f;

            currentDensity = targetDensity;
        }
        else
        {
            targetDensity =
                weather != null &&
                weather.CurrentWeather == WeatherType.Fog
                    ? fogDensity
                    : 0f;

            currentDensity =
                Mathf.MoveTowards(
                    currentDensity,
                    targetDensity,
                    fadeSpeed * Time.deltaTime);
        }

        ApplyAllProperties();
    }

    private void ResolveReferences()
    {
        if (weather == null)
            weather = FindFirstObjectByType<WeatherManager>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (fogRenderer == null)
            fogRenderer = GetComponent<Renderer>();

        if (fogRenderer != null &&
            fogMaterial != null &&
            fogRenderer.sharedMaterial != fogMaterial)
        {
            fogRenderer.sharedMaterial = fogMaterial;
        }

        propertyBlock ??= new MaterialPropertyBlock();
    }

    private void UpdateTransform()
    {
        if (followCamera && targetCamera != null)
        {
            Vector3 forward = targetCamera.transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude > 0.001f)
                forward.Normalize();

            Vector3 target =
                targetCamera.transform.position +
                forward * cameraForwardOffset;

            target.y = heightAboveGround;
            transform.position = target;
        }
        else
        {
            Vector3 p = transform.position;
            p.y = heightAboveGround;
            transform.position = p;
        }

        float scale = Mathf.Max(1f, areaSize / 10f);
        transform.localScale = new Vector3(scale, 1f, scale);
    }

    private void EnsureGeneratedTextures()
    {
        if (!generateMissingNoise)
            return;

        int hash = ComputeGeneratedSettingsHash();

        if (hash != generatedSettingsHash)
        {
            DestroyGeneratedTextures();
            generatedSettingsHash = hash;
        }

        if (shapeNoiseMap == null && generatedShapeNoise == null)
        {
            generatedShapeNoise =
                GenerateNoiseTexture(
                    "Runtime_FogShapeNoise",
                    generatedShapeNoiseScale,
                    generatedNoiseSeed,
                    false);
        }

        if (detailNoiseMap == null && generatedDetailNoise == null)
        {
            generatedDetailNoise =
                GenerateNoiseTexture(
                    "Runtime_FogDetailNoise",
                    generatedDetailNoiseScale,
                    generatedNoiseSeed + 7919,
                    true);
        }
    }

    private Texture2D GenerateNoiseTexture(
        string textureName,
        float scale,
        int seed,
        bool detail)
    {
        int size = Mathf.Clamp(generatedNoiseResolution, 64, 512);

        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                true,
                true);

        texture.name = textureName;
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        System.Random random = new System.Random(seed);

        float offsetX = (float)random.NextDouble() * 1000f;
        float offsetY = (float)random.NextDouble() * 1000f;

        for (int y = 0; y < size; y++)
        {
            float v = y / (float)size;

            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;

                float n1 =
                    SeamlessPerlin(
                        u,
                        v,
                        scale,
                        offsetX,
                        offsetY);

                float n2 =
                    SeamlessPerlin(
                        u + 0.173f,
                        v + 0.319f,
                        scale * (detail ? 1.91f : 1.63f),
                        offsetX + 53.4f,
                        offsetY + 21.7f);

                float n3 =
                    SeamlessPerlin(
                        u + 0.417f,
                        v + 0.127f,
                        scale * (detail ? 3.17f : 0.53f),
                        offsetX + 11.8f,
                        offsetY + 88.2f);

                float noise =
                    detail
                        ? n1 * 0.52f + n2 * 0.33f + n3 * 0.15f
                        : n1 * 0.64f + n2 * 0.23f + n3 * 0.13f;

                byte value =
                    (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(noise) * 255f);

                pixels[y * size + x] =
                    new Color32(value, value, value, 255);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static float SeamlessPerlin(
        float u,
        float v,
        float frequency,
        float offsetX,
        float offsetY)
    {
        u = Mathf.Repeat(u, 1f);
        v = Mathf.Repeat(v, 1f);

        float n00 = Mathf.PerlinNoise(offsetX + u * frequency, offsetY + v * frequency);
        float n10 = Mathf.PerlinNoise(offsetX + (u - 1f) * frequency, offsetY + v * frequency);
        float n01 = Mathf.PerlinNoise(offsetX + u * frequency, offsetY + (v - 1f) * frequency);
        float n11 = Mathf.PerlinNoise(offsetX + (u - 1f) * frequency, offsetY + (v - 1f) * frequency);

        float nx0 = Mathf.Lerp(n00, n10, u);
        float nx1 = Mathf.Lerp(n01, n11, u);
        return Mathf.Lerp(nx0, nx1, v);
    }

    private int ComputeGeneratedSettingsHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + generatedNoiseResolution;
            hash = hash * 31 + generatedNoiseSeed;
            hash = hash * 31 + generatedShapeNoiseScale.GetHashCode();
            hash = hash * 31 + generatedDetailNoiseScale.GetHashCode();
            return hash;
        }
    }

    private void ApplyAllProperties()
    {
        if (fogRenderer == null)
            return;

        propertyBlock ??= new MaterialPropertyBlock();
        fogRenderer.GetPropertyBlock(propertyBlock);

        Texture shape =
            shapeNoiseMap != null
                ? shapeNoiseMap
                : generatedShapeNoise;

        Texture detail =
            detailNoiseMap != null
                ? detailNoiseMap
                : generatedDetailNoise;

        if (shape != null)
            propertyBlock.SetTexture(ShapeNoiseTex, shape);

        if (detail != null)
            propertyBlock.SetTexture(DetailNoiseTex, detail);

        propertyBlock.SetFloat(Density, Mathf.Clamp01(currentDensity));
        propertyBlock.SetFloat(FogOpacity, fogOpacity);
        propertyBlock.SetColor(FogColor, fogColor);
        propertyBlock.SetFloat(PatchSize, Mathf.Max(1f, fogPatchSize));
        propertyBlock.SetFloat(EdgeSoftness, edgeSoftness);
        propertyBlock.SetFloat(FogContrast, fogContrast);
        propertyBlock.SetFloat(EdgeDetailScale, edgeDetailScale);
        propertyBlock.SetFloat(EdgeDetailStrength, edgeDetailStrength);
        propertyBlock.SetFloat(MicroDetailScale, microDetailScale);
        propertyBlock.SetFloat(MicroDetailStrength, microDetailStrength);
        propertyBlock.SetVector(WindSpeed, new Vector4(windSpeed.x, windSpeed.y, 0f, 0f));
        propertyBlock.SetFloat(MorphSpeed, morphSpeed);
        propertyBlock.SetFloat(MorphStrength, morphStrength);

        fogRenderer.SetPropertyBlock(propertyBlock);
    }

    void OnValidate()
    {
        generatedNoiseResolution = Mathf.Clamp(generatedNoiseResolution, 64, 512);
        generatedSettingsHash = 0;

        if (!Application.isPlaying)
        {
            ResolveReferences();
            EnsureGeneratedTextures();
            ApplyAllProperties();
        }
    }

    private void DestroyGeneratedTextures()
    {
        DestroyTexture(ref generatedShapeNoise);
        DestroyTexture(ref generatedDetailNoise);
    }

    private static void DestroyTexture(ref Texture2D texture)
    {
        if (texture == null)
            return;

        if (Application.isPlaying)
            Destroy(texture);
        else
            DestroyImmediate(texture);

        texture = null;
    }

    void OnDestroy()
    {
        DestroyGeneratedTextures();
    }
}
