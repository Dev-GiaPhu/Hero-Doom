using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class CombatKnockback : MonoBehaviour
{
    [Min(0.05f)] public float duration = 0.32f;
    [Range(0, 0.9f)] public float resistance;
    [Min(0.1f)] public float maximumSpeed = 16f;
    public bool IsActive => remaining > 0;
    private NavMeshAgent agent;
    private Vector3 velocity;
    private float remaining;

    void Awake() { agent = GetComponent<NavMeshAgent>(); }

    public void Push(Vector3 impulse)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        impulse.y = 0;
        if (impulse.sqrMagnitude < 0.001f) return;
        velocity = Vector3.ClampMagnitude(impulse * (1 - resistance), maximumSpeed);
        remaining = Mathf.Max(0.05f, duration);
        agent.isStopped = true;
        agent.ResetPath();
    }

    void Update()
    {
        if (!IsActive) return;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) { remaining = 0; return; }
        float dt = Mathf.Min(Time.deltaTime, remaining);
        float damping = remaining / Mathf.Max(0.05f, duration);
        Vector3 start = transform.position;
        Vector3 next = start + velocity * damping * dt;
        // Keep the impulse on the navigation surface, including at walls and shorelines.
        if (NavMesh.Raycast(start, next, out NavMeshHit hit, agent.areaMask))
            next = Vector3.MoveTowards(hit.position, start, 0.05f);
        agent.Move(next - start);
        remaining -= dt;
    }

    void OnDisable() { remaining = 0; }
}
