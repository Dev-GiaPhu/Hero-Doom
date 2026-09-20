using System.Collections.Generic;
using UnityEngine;

public class ExperienceOrb : MonoBehaviour
{
    public static readonly HashSet<ExperienceOrb> Active = new();
    public float value = 3f;

    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    public void Collect(HeroBotAI hero)
    {
        if (hero == null || !hero.IsAlive) return;
        hero.AddExperience(value);
        Destroy(gameObject);
    }

    public static void Spawn(Vector3 position, float amount)
    {
        GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "HeroXP";
        orb.transform.position = position + Vector3.up * 0.3f;
        orb.transform.localScale = Vector3.one * 0.25f;

        Collider col = orb.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        ExperienceOrb xp = orb.AddComponent<ExperienceOrb>();
        xp.value = Mathf.Max(1f, amount);

        Renderer renderer = orb.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                Material mat = new Material(shader);
                mat.color = new Color(0.25f, 0.9f, 1f, 1f);
                renderer.material = mat;
            }
        }
    }

    void Update()
    {
        transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
    }
}
