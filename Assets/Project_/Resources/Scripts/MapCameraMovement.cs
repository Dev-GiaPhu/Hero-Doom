using UnityEngine;
using UnityEngine.InputSystem;

public class MapCameraMovement : MonoBehaviour
{
    [Header("WASD Camera")]
    [Tooltip("Tốc độ camera trên mặt phẳng XZ.")]
    [Min(0f)] public float moveSpeed = 12f;
    [Tooltip("Hệ số tốc độ khi giữ Shift.")]
    [Min(1f)] public float sprintMultiplier = 2f;
    [Header("Smooth")]
    [Tooltip("Thời gian camera tăng/giảm tốc. Nhỏ = phản hồi nhanh, lớn = trôi mềm hơn.")]
    [Range(0.02f, 1f)] public float smoothTime = 0.16f;

    private Vector3 currentVelocity;
    private Vector3 smoothVelocity;

    void Update()
    {
        if (Keyboard.current == null) return;

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;

        if (input.sqrMagnitude > 1f) input.Normalize();

        float speed = moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)
            speed *= sprintMultiplier;

        // Smooth velocity thay vì lerp position để camera tăng tốc và hãm tự nhiên,
        // đồng thời vẫn giữ nguyên độ cao và góc camera.
        Vector3 targetVelocity = new Vector3(input.x, 0f, input.y) * speed;
        currentVelocity = Vector3.SmoothDamp(currentVelocity, targetVelocity, ref smoothVelocity, smoothTime);
        transform.position += currentVelocity * Time.deltaTime;
    }
}
