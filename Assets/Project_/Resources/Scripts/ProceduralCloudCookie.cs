using UnityEngine;
using UnityEngine.Rendering.Universal; 

[RequireComponent(typeof(Light))]
public class ProceduralCloudCookie : MonoBehaviour
{
    private Light directionalLight;
    private UniversalAdditionalLightData urpLightData;
    
    [Header("Cloud Shadow Settings")]
    public int resolution = 256;
    public float scale = 5f;
    [Range(0f, 1f)] 
    public float cloudDarkness = 0.3f; // Độ nhạt/đậm của bóng mây (0.3 là bóng rất nhẹ)

    [Header("Wind Speed")]
    public float scrollSpeedX = 0.05f;
    public float scrollSpeedY = 0.02f;

    private Vector2 currentOffset;

    void Start()
    {
        directionalLight = GetComponent<Light>();
        urpLightData = GetComponent<UniversalAdditionalLightData>();

        // Tự động sinh ảnh mây và gắn vào Cookie của đèn
        directionalLight.cookie = GenerateCloudCookie();
        
        // Cài đặt độ to của mây bao phủ bản đồ
        if (urpLightData != null)
        {
            urpLightData.lightCookieSize = new Vector2(50f, 50f); 
        }
    }

    void Update()
    {
        // Cuộn Cookie để tạo hiệu ứng gió thổi mây trôi
        if (urpLightData != null)
        {
            currentOffset.x += scrollSpeedX * Time.deltaTime;
            currentOffset.y += scrollSpeedY * Time.deltaTime;
            urpLightData.lightCookieOffset = currentOffset;
        }
    }

    Texture2D GenerateCloudCookie()
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat; // Để mây cuộn không bị đứt quãng

        Color32[] pixels = new Color32[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float xCoord = (float)x / resolution * scale;
                float yCoord = (float)y / resolution * scale;
                
                float noise = Mathf.PerlinNoise(xCoord, yCoord);
                
                // Trắng (255) là ánh sáng chiếu qua 100%. Xám là ánh sáng bị cản lại thành bóng mờ.
                byte grayValue = (byte)Mathf.Lerp(255, 255 * (1f - cloudDarkness), noise);
                
                pixels[y * resolution + x] = new Color32(grayValue, grayValue, grayValue, 255);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false);
        return tex;
    }
}