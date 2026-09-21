using UnityEngine;

[ExecuteAlways]
public class FogLayerController : MonoBehaviour
{
    [Header("Scene References")]
    public WeatherManager weather;
    public Camera targetCamera;
    public Renderer fogRenderer;
    public Material fogMaterial;

    [Header("Noise Map")]
    [Tooltip("Kéo noise texture riêng vào đây nếu muốn. Để trống thì script tự tạo noise map.")]
    public Texture2D noiseMap;
    public bool generateNoiseIfMissing = true;
    [Range(64, 512)] public int generatedNoiseResolution = 256;
    [Range(0.5f, 12f)] public float generatedNoiseScale = 5f;
    public int generatedNoiseSeed = 1337;

    [Header("Fog Layer")]
    [Min(10f)] public float areaSize = 80f;
    [Min(0f)] public float heightAboveGround = 3f;
    [Min(0f)] public float cameraForwardOffset = 8f;
    public bool followCamera = true;

    [Header("Density / Transition")]
    [Range(0f, 1f)] public float fogDensity = 0.42f;
    [Min(0.05f)] public float fadeSpeed = 0.35f;

    [Header("Editor Preview")]
    public bool previewInEditMode = true;
    [Range(0f, 1f)] public float editModePreviewDensity = 0.28f;

    private static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
    private static readonly int Density = Shader.PropertyToID("_Density");

    private MaterialPropertyBlock propertyBlock;
    private Texture2D generatedNoise;
    private float currentDensity;

    void Awake()
    {
        ResolveReferences();
        EnsureNoiseMap();
        ApplyStaticSetup();
    }

    void OnEnable()
    {
        ResolveReferences();
        EnsureNoiseMap();
        ApplyStaticSetup();
    }

    void Update()
    {
        ResolveReferences();
        EnsureNoiseMap();

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

        if (!Application.isPlaying)
        {
            currentDensity =
                previewInEditMode
                    ? editModePreviewDensity
                    : 0f;
        }
        else
        {
            float targetDensity =
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

        ApplyPropertyBlock();
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

    private void EnsureNoiseMap()
    {
        if (noiseMap != null ||
            !generateNoiseIfMissing ||
            generatedNoise != null)
        {
            return;
        }

        int size =
            Mathf.Clamp(
                generatedNoiseResolution,
                64,
                512);

        generatedNoise =
            new Texture2D(
                size,
                size,
                TextureFormat.R8,
                true,
                true);

        generatedNoise.name =
            "Runtime_FogNoiseMap";

        generatedNoise.wrapMode =
            TextureWrapMode.Repeat;

        generatedNoise.filterMode =
            FilterMode.Bilinear;

        Color32[] pixels =
            new Color32[size * size];

        System.Random random =
            new System.Random(
                generatedNoiseSeed);

        float offsetX =
            (float)random.NextDouble() *
            1000f;

        float offsetY =
            (float)random.NextDouble() *
            1000f;

        for (int y = 0; y < size; y++)
        {
            float v =
                y / (float)size;

            for (int x = 0; x < size; x++)
            {
                float u =
                    x / (float)size;

                float n1 =
                    Mathf.PerlinNoise(
                        offsetX +
                        u * generatedNoiseScale,
                        offsetY +
                        v * generatedNoiseScale);

                float n2 =
                    Mathf.PerlinNoise(
                        offsetX + 91.7f +
                        u * generatedNoiseScale * 2.03f,
                        offsetY + 37.2f +
                        v * generatedNoiseScale * 2.03f);

                float n3 =
                    Mathf.PerlinNoise(
                        offsetX + 13.4f +
                        u * generatedNoiseScale * 0.51f,
                        offsetY + 67.8f +
                        v * generatedNoiseScale * 0.51f);

                float noise =
                    Mathf.Clamp01(
                        n1 * 0.58f +
                        n2 * 0.27f +
                        n3 * 0.15f);

                byte value =
                    (byte)Mathf.RoundToInt(
                        noise * 255f);

                pixels[y * size + x] =
                    new Color32(
                        value,
                        value,
                        value,
                        255);
            }
        }

        generatedNoise.SetPixels32(pixels);
        generatedNoise.Apply(true, false);
    }

    private void ApplyStaticSetup()
    {
        if (fogRenderer != null &&
            fogMaterial != null)
        {
            fogRenderer.sharedMaterial =
                fogMaterial;
        }

        ApplyPropertyBlock();
    }

    private void ApplyPropertyBlock()
    {
        if (fogRenderer == null)
            return;

        propertyBlock ??=
            new MaterialPropertyBlock();

        fogRenderer.GetPropertyBlock(
            propertyBlock);

        Texture texture =
            noiseMap != null
                ? noiseMap
                : generatedNoise;

        if (texture != null)
        {
            propertyBlock.SetTexture(
                NoiseTex,
                texture);
        }

        propertyBlock.SetFloat(
            Density,
            Mathf.Clamp01(
                currentDensity));

        fogRenderer.SetPropertyBlock(
            propertyBlock);
    }

    void OnDestroy()
    {
        if (generatedNoise == null)
            return;

        if (Application.isPlaying)
            Destroy(generatedNoise);
        else
            DestroyImmediate(generatedNoise);
    }
}
