using UnityEngine;

public class DayNightCycle3D : MonoBehaviour
{
    public Light directionalLight; 
    public float dayDuration = 120f; // Thời gian 1 ngày trong game (giây)
    
    [Header("Sun Settings")]
    public Gradient sunColor; 
    public AnimationCurve sunIntensity; // Đỉnh cong cao nhất ở giữa trưa (0.5), thấp dần về đêm

    private float timeOfDay = 0f;

    void Update()
    {
        timeOfDay += Time.deltaTime;
        float timeFraction = timeOfDay / dayDuration;
        if (timeFraction >= 1f) timeOfDay = 0f;

        // 1. Xoay góc Mặt trời (Trục X xoay từ -90 đến 270 để giả lập quỹ đạo)
        float sunAngle = Mathf.Lerp(-90f, 270f, timeFraction);
        directionalLight.transform.rotation = Quaternion.Euler(sunAngle, 30f, 0f);

        // 2. Cập nhật màu và độ sáng
        directionalLight.color = sunColor.Evaluate(timeFraction);
        directionalLight.intensity = sunIntensity.Evaluate(timeFraction);
    }
}