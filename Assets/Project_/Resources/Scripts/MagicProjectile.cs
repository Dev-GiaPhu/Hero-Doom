using UnityEngine;

public class MagicProjectile : MonoBehaviour
{
    private HeroBotAI target;
    private MonsterAI source;
    private float damage;
    private float knockback;
    private float speed = 12f;
    private float life = 4f;

    public static void Spawn(Vector3 origin, HeroBotAI target, MonsterAI source, float damage, float knockback)
    {
        if (target == null || !target.IsAlive) return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "MageProjectile";
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.22f;

        Collider col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Renderer renderer = go.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (renderer != null && shader != null)
        {
            Material mat = new Material(shader);
            mat.color = new Color(0.35f, 0.65f, 1f, 1f);
            renderer.material = mat;
        }

        MagicProjectile projectile = go.AddComponent<MagicProjectile>();
        projectile.target = target;
        projectile.source = source;
        projectile.damage = damage;
        projectile.knockback = knockback;
    }

    public static void SpawnLightning(Vector3 start, Vector3 end)
    {
        GameObject go = new GameObject("MageLightning");
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.positionCount = 6;
        line.widthMultiplier = 0.055f;
        line.useWorldSpace = true;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            Material mat = new Material(shader);
            mat.color = new Color(0.55f, 0.78f, 1f, 1f);
            line.material = mat;
        }

        Vector3 direction = end - start;
        for (int i = 0; i < line.positionCount; i++)
        {
            float t = i / (float)(line.positionCount - 1);
            Vector3 point = Vector3.Lerp(start, end, t);
            if (i > 0 && i < line.positionCount - 1)
                point += new Vector3(Random.Range(-0.18f, 0.18f), Random.Range(-0.12f, 0.2f), Random.Range(-0.18f, 0.18f));
            line.SetPosition(i, point);
        }

        Object.Destroy(go, 0.16f);
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

        transform.position += delta.normalized * step;
    }
}
