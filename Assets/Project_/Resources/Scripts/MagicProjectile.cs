using UnityEngine;

public class MagicProjectile : MonoBehaviour
{
    private HeroBotAI target;
    private MonsterAI source;
    private float damage;
    private float knockback;
    private float speed = 12f;
    private float life = 4f;

    public static void Spawn(Vector3 origin, HeroBotAI target, MonsterAI source, float damage, float knockback, GameObject prefab = null)
    {
        if (target == null || !target.IsAlive) return;

        if (prefab == null) return;
        Vector3 direction = target.transform.position + Vector3.up - origin;
        Quaternion rotation = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        GameObject go = Object.Instantiate(prefab, origin, rotation);

        // Movement and hits are handled here, independently of prefab physics.
        foreach (Collider col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
        foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>(true))
        {
            body.useGravity = false;
            body.isKinematic = true;
        }

        MagicProjectile projectile = go.GetComponent<MagicProjectile>();
        if (projectile == null)
        {
            Debug.LogError("Magic projectile prefab needs a MagicProjectile component.", prefab);
            Object.Destroy(go);
            return;
        }
        projectile.target = target;
        projectile.source = source;
        projectile.damage = damage;
        projectile.knockback = knockback;
        projectile.life = 4f;
        projectile.enabled = true;
        go.SetActive(true);
    }

    public static void SpawnLightning(LineRenderer prefab, Vector3 start, Vector3 end)
    {
        if (prefab == null) return;
        LineRenderer line = Object.Instantiate(prefab);
        line.useWorldSpace = true;

        Vector3 direction = end - start;
        for (int i = 0; i < line.positionCount; i++)
        {
            float t = i / (float)(line.positionCount - 1);
            Vector3 point = Vector3.Lerp(start, end, t);
            if (i > 0 && i < line.positionCount - 1)
                point += new Vector3(Random.Range(-0.18f, 0.18f), Random.Range(-0.12f, 0.2f), Random.Range(-0.18f, 0.18f));
            line.SetPosition(i, point);
        }

        Object.Destroy(line.gameObject, 0.16f);
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f || target == null || !target.IsAlive)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 destination = target.transform.position + Vector3.up;
        Vector3 delta = destination - transform.position;
        float step = speed * Time.deltaTime;

        if (delta.magnitude <= step + 0.15f)
        {
            Vector3 push = (target.transform.position - transform.position).normalized;
            target.TakeDamage(damage, source, push * knockback, false);
            Destroy(gameObject);
            return;
        }

        transform.rotation = Quaternion.LookRotation(delta);
        transform.position += delta.normalized * step;
    }
}
