using UnityEngine;
using UnityEngine.InputSystem;

public class MapCameraMovement : MonoBehaviour
{
    [Header("WASD Camera")]
    [Min(0f)] public float moveSpeed = 12f;
    [Min(1f)] public float sprintMultiplier = 2f;

    [Header("Smooth")]
    [Range(0.02f, 1f)] public float smoothTime = 0.16f;

    private Vector3 currentVelocity;
    private Vector3 smoothVelocity;
    private InfiniteMapGenerator map;

    void Start() => map = FindFirstObjectByType<InfiniteMapGenerator>();

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
        if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed) speed *= sprintMultiplier;

        Vector3 targetVelocity = new Vector3(input.x, 0f, input.y) * speed;
        currentVelocity = Vector3.SmoothDamp(currentVelocity, targetVelocity, ref smoothVelocity, smoothTime);
        Vector3 next = transform.position + currentVelocity * Time.deltaTime;

        if (map != null)
        {
            float limit = Mathf.Max(0f, map.WorldHalfSize - map.maxChunkSize);
            next.x = Mathf.Clamp(next.x, -limit, limit);
            next.z = Mathf.Clamp(next.z, -limit, limit);
        }

        transform.position = next;
    }
}
