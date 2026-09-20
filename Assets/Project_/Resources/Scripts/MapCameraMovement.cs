using UnityEngine;
using UnityEngine.InputSystem;

public class MapCameraMovement : MonoBehaviour
{
    [Header("WASD Camera")]
    [Tooltip("Tốc độ camera trên mặt phẳng XZ.")]
    [Min(0f)] public float moveSpeed = 12f;
    [Tooltip("Hệ số tốc độ khi giữ Shift.")]
    [Min(1f)] public float sprintMultiplier = 2f;

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

        // WASD luôn chạy theo trục map, không làm thay đổi độ cao/góc camera.
        Vector3 delta = new Vector3(input.x, 0f, input.y) * speed * Time.deltaTime;
        transform.position += delta;
    }
}
