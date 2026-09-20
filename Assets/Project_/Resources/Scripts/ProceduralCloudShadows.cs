using UnityEngine;

public class ProceduralCloudShadows : MonoBehaviour
{
    public int resolution = 256;
    public float scale = 5f;
    [Range(0f, 1f)] public float density = 0.6f;
    public float scrollSpeedX = 0.05f;
    public float scrollSpeedY = 0.02f;

    private Material cloudMaterial;
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");

    void Start()
    {
        cloudMaterial = GetComponent<Renderer>().material;
        
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;

        // Tối ưu: Dùng mảng 1 chiều SetPixels32 thay vì gọi SetPixel liên tục
        Color32[] pixels = new Color32[resolution * resolution];
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float xCoord = (float)x / resolution * scale;
                float yCoord = (float)y / resolution * scale;
                
                float noise = Mathf.PerlinNoise(xCoord, yCoord);
                float alpha = Mathf.Clamp01(noise - (1f - density));
                
                byte a = (byte)(alpha * 255);
                pixels[y * resolution + x] = new Color32(51, 51, 51, a);
            }
        }
        
        tex.SetPixels32(pixels);
        tex.Apply(false); // Tắt Mipmaps để giảm tải bộ nhớ GPU
        
        if (cloudMaterial.HasProperty(BaseMap))
            cloudMaterial.SetTexture(BaseMap, tex);
        else if (cloudMaterial.HasProperty(MainTex))
            cloudMaterial.SetTexture(MainTex, tex);
        else
            cloudMaterial.mainTexture = tex;
    }

    void Update()
    {
        float offsetX = Time.time * scrollSpeedX;
        float offsetY = Time.time * scrollSpeedY;
        Vector2 offset = new Vector2(offsetX, offsetY);
        
        if (cloudMaterial.HasProperty(BaseMap))
            cloudMaterial.SetTextureOffset(BaseMap, offset);
        else if (cloudMaterial.HasProperty(MainTex))
            cloudMaterial.SetTextureOffset(MainTex, offset);
        else
            cloudMaterial.mainTextureOffset = offset;
    }
}