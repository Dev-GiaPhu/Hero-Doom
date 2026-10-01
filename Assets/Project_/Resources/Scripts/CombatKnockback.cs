using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class CombatKnockback : MonoBehaviour
{
    [Min(0.05f)] public float duration = 0.32f;
    [Range(0, 0.9f)] public float resistance;
    [Min(0.1f)] public float maximumSpeed = 16f;
    [Tooltip("Allows combat knockback to move off the NavMesh, so units can be thrown into water.")]
    public bool allowOffNavMesh = true;
    [Min(0.1f)] public float navMeshRecoveryRadius = 3f;

    public bool IsActive => remaining > 0f;

    private NavMeshAgent agent;
    private Vector3 velocity;
    private float remaining;
    private bool previousUpdatePosition;
    private bool previousUpdateRotation;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public void Push(Vector3 impulse)
    {
        if (agent == null || !agent.enabled) return;

        impulse.y = 0f;
        if (impulse.sqrMagnitude < 0.001f) return;

        velocity = Vector3.ClampMagnitude(impulse * (1f - resistance), maximumSpeed);
        remaining = Mathf.Max(0.05f, duration);

        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        if (allowOffNavMesh)
        {
            previousUpdatePosition = agent.updatePosition;
            previousUpdateRotation = agent.updateRotation;
            agent.updatePosition = false;
            agent.updateRotation = false;
        }
    }

    void Update()
    {
        if (!IsActive) return;

        float dt = Mathf.Min(Time.deltaTime, remaining);
        float damping = remaining / Mathf.Max(0.05f, duration);
        Vector3 displacement = velocity * damping * dt;

        if (allowOffNavMesh)
        {
            // Deliberately move the transform itself. This lets the unit cross a NavMesh shoreline
            // and enter a WaterHazard trigger instead of treating the shore as a wall.
            transform.position += displacement;

            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.nextPosition = transform.position;
        }
        else if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(displacement);
        }

        remaining -= dt;
        if (remaining <= 0f)
            FinishKnockback();
    }

    private void FinishKnockback()
    {
        remaining = 0f;
        velocity = Vector3.zero;

        if (agent == null || !agent.enabled) return;

        if (allowOffNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshRecoveryRadius, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                agent.Warp(hit.position);
            }

            agent.updatePosition = previousUpdatePosition;
            agent.updateRotation = previousUpdateRotation;
        }

        if (agent.isOnNavMesh)
            agent.isStopped = false;
    }

    void OnDisable()
    {
        remaining = 0f;
        velocity = Vector3.zero;

        if (agent != null)
        {
            agent.updatePosition = true;
            agent.updateRotation = true;
        }
    }
}
