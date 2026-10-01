using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Edit-time authoring only. Nothing in this file is included in player builds.
[InitializeOnLoad]
public static class HeroDoomCombatAuthoring
{
    private const string Root = "Assets/Project_/Resources";
    private const string Work = "Temp/HeroDoomWork";
    private static readonly Color Ink = new Color(0.055f, 0.10f, 0.11f, 1);
    private static readonly Color Card = new Color(0.085f, 0.15f, 0.16f, 1);
    private static readonly Color Gold = new Color(1f, 0.79f, 0.38f, 1);
    private static readonly Color Mint = new Color(0.4f, 0.85f, 0.77f, 1);
    static HeroDoomCombatAuthoring() { EditorApplication.update += PollRequest; }

    private static void PollRequest()
    {
        string request = Work + "/request.txt";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string action = File.ReadAllText(request).Trim();
        File.Delete(request);
        try
        {
            if (action == "build") Build();
            else if (action == "validate") Validate();
            else if (action == "test") RunChecks();
            else throw new Exception("Unknown authoring request: " + action);
        }
        catch (Exception e) { File.WriteAllText(Work + "/result.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Hero Doom/Author Combat Assets and Upgrade Panel")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Leave Play Mode before authoring.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity") throw new Exception("Open SampleScene before authoring.");
        Directory.CreateDirectory(Work);
        string backup = Work + "/SampleScene-before-authoring.unity";
        if (!File.Exists(backup)) File.Copy(scene.path, backup);
        EnsureFolder(Root + "/Prefabs/Combat"); EnsureFolder(Root + "/Upgrades");
        EnsureFolder(Root + "/Materials/Combat"); EnsureFolder(Root + "/Image/Upgrades");
        HeroDoomGameManager manager = Object.FindFirstObjectByType<HeroDoomGameManager>();
        PlayerSpawner spawner = Object.FindFirstObjectByType<PlayerSpawner>();
        if (manager == null || spawner == null) throw new Exception("Missing GameManager or PlayerSpawner.");
        var panel = GameObject.Find("Canvas/Right Panel");
        if (panel == null) throw new Exception("Missing Canvas/Right Panel.");
        Transform existingUpgradePanel = panel.transform.Find("Summoner Upgrades");
        if (existingUpgradePanel != null)
            Undo.DestroyObjectImmediate(existingUpgradePanel.gameObject);
        DamagePopup damage = CreateDamage();
        ExperienceOrb xp = CreateXP();
        LineRenderer lightning = CreateLightning();
        Material ring = CreateRingMaterial();
        GameObject ringPrefab = CreateRing(ring);
        HeroUpgradeDefinition[] heroDefs = CreateHeroDefinitions();
        HeroUpgradeThought thought = CreateThought(heroDefs);
        var hud = Object.FindFirstObjectByType<MonsterHUDController>();
        SummonerUpgrades upgrades = manager.GetComponent<SummonerUpgrades>();
        if (upgrades == null) upgrades = Undo.AddComponent<SummonerUpgrades>(manager.gameObject);
        upgrades.gameManager = manager; upgrades.spawner = spawner; upgrades.maxAliveMonsters = 36;
        for (int i = 0; i < 4; i++)
        {
            Sprite icon = hud != null && hud.slots.Length > i ? hud.slots[i].normalSprite : null;
            upgrades.definitions[i] = CreateMonsterDefinition(i, icon);
            string path = AssetDatabase.GetAssetPath(spawner.GetPrefab(i));
            if (string.IsNullOrEmpty(path)) throw new Exception("Monster prefab slot " + i + " is empty.");
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                MonsterAI ai = root.GetComponent<MonsterAI>();
                AuthorPhysics(root, false);
                ai.role = (MonsterRole)i;
                EnsureMonsterHealthBar(root);
                ai.damagePopupPrefab = damage; ai.experienceOrbPrefab = xp;
                ai.knockbackMotor = root.GetComponent<CombatKnockback>();
                ai.knockbackMotor.resistance = i == 2 ? 0.4f : 0;
                ai.spawnProtection = AttachProtection(root, ringPrefab, 1.5f);
                ai.knockbackForce = new[] { 6f, 4f, 10f, 5.5f }[i];
                if (i == 1)
                {
                    ai.attackRange = 8; ai.preferredRange = 6; ai.moveSpeed = 3.8f;
                    ai.retreatDuration = 0.65f; ai.retreatCooldown = 2.5f;
                    ai.lightningPrefab = lightning;
                    if (ai.mageProjectilePrefab != null) AuthorProjectile(ai.mageProjectilePrefab);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        string heroPath = AssetDatabase.GetAssetPath(manager.heroPrefab);
        GameObject heroRoot = PrefabUtility.LoadPrefabContents(heroPath);
        try
        {
            AuthorHero(heroRoot, damage, thought, heroDefs, ringPrefab);
            PrefabUtility.SaveAsPrefabAsset(heroRoot, heroPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(heroRoot); }
        foreach (HeroBotAI hero in Object.FindObjectsByType<HeroBotAI>(FindObjectsSortMode.None))
        {
            AuthorHero(hero.gameObject, damage, thought, heroDefs, ringPrefab);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hero);
        }
        spawner.upgrades = upgrades;
        EditorUtility.SetDirty(spawner); EditorUtility.SetDirty(upgrades);
        CreatePanel(panel, upgrades);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = panel;
        Validate();
        File.AppendAllText(Work + "/result.txt", "\nAssets and scene saved.\n");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void AuthorPhysics(GameObject go, bool hero)
    {
        if (go.GetComponent<Collider>() == null)
        { var capsule = go.AddComponent<CapsuleCollider>(); capsule.center = Vector3.up; capsule.height = 2; capsule.radius = 0.45f; }
        Rigidbody body = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false;
        NavMeshAgent agent = go.GetComponent<NavMeshAgent>();
        if (agent == null)
            agent = go.AddComponent<NavMeshAgent>();
        if (agent == null)
            throw new Exception("Could not add NavMeshAgent to " + go.name);

        agent.radius = hero ? 0.48f : 0.42f; agent.height = 2; agent.angularSpeed = 720;
        agent.acceleration = 24; agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = hero ? 30 : 50;
        if (go.GetComponent<CombatKnockback>() == null) go.AddComponent<CombatKnockback>();
    }

    private static void AuthorHero(GameObject root, DamagePopup damage, HeroUpgradeThought thought, HeroUpgradeDefinition[] defs, GameObject ring)
    {
        AuthorPhysics(root, true);
        HeroBotAI hero = root.GetComponent<HeroBotAI>();
        hero.damagePopupPrefab = damage; hero.upgradeThoughtPrefab = thought; hero.upgradeDefinitions = defs;
        hero.knockbackMotor = root.GetComponent<CombatKnockback>();
        hero.spawnProtection = AttachProtection(root, ring, 2.5f);
        hero.knockbackForce = 7; hero.areaKnockbackForce = 9;
        EditorUtility.SetDirty(hero);
    }

    private static SpawnProtection AttachProtection(GameObject root, GameObject ringPrefab, float duration)
    {
        SpawnProtection protection = root.GetComponent<SpawnProtection>() ?? root.AddComponent<SpawnProtection>();
        protection.duration = duration;
        if (protection.ringVisual == null)
        {
            GameObject ring = (GameObject)PrefabUtility.InstantiatePrefab(ringPrefab, root.transform);
            ring.name = "Spawn Protection Ring";
            protection.ringVisual = ring; protection.ringRenderer = ring.GetComponent<Renderer>();
        }
        EditorUtility.SetDirty(protection);
        return protection;
    }

    private static void AuthorProjectile(GameObject prefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<MagicProjectile>() == null) root.AddComponent<MagicProjectile>();
            foreach (Collider col in root.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void EnsureMonsterHealthBar(GameObject root)
    {
        MonsterAI monster = root.GetComponent<MonsterAI>();
        if (monster == null) return;

        Transform existing = root.transform.Find("Monster Health Bar");
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject canvasGo = new GameObject(
            "Monster Health Bar",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasGroup),
            typeof(MonsterHealthBar));

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.SetParent(root.transform, false);
        canvasRect.sizeDelta = new Vector2(220f, 22f);
        canvasRect.localPosition = new Vector3(0f, 2.25f, 0f);
        canvasRect.localScale = Vector3.one * 0.01f;

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 105;

        CanvasGroup group = canvasGo.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        GameObject bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
        RectTransform bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.SetParent(canvasRect, false);
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bg = bgGo.GetComponent<Image>();
        bg.color = new Color(0.04f, 0.055f, 0.06f, 0.92f);
        bg.raycastTarget = false;

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        RectTransform fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.SetParent(bgRect, false);
        fillRect.anchorMin = new Vector2(0.04f, 0.16f);
        fillRect.anchorMax = new Vector2(0.96f, 0.84f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillGo.GetComponent<Image>();
        fill.color = new Color(0.25f, 0.9f, 0.35f, 1f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 1f;
        fill.raycastTarget = false;

        MonsterHealthBar bar = canvasGo.GetComponent<MonsterHealthBar>();
        bar.monster = monster;
        bar.canvasGroup = group;
        bar.fill = fill;
        bar.visualRoot = canvasRect;
        bar.worldOffset = new Vector3(0f, 2.25f, 0f);
        bar.fadeInDuration = 0.12f;
        bar.fadeOutDuration = 0.16f;
        bar.hiddenScale = 0.78f;
    }

    private static DamagePopup CreateDamage()
    {
        GameObject go = new GameObject("Damage Number", typeof(RectTransform), typeof(TextMeshPro), typeof(DamagePopup));
        TextMeshPro text = go.GetComponent<TextMeshPro>();
        text.font = TMP_Settings.defaultFontAsset; text.text = "128"; text.fontSize = 9f;
        text.fontStyle = FontStyles.Bold; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.color = Gold;
        text.outlineWidth = 0.18f; text.outlineColor = new Color(0.05f, 0.03f, 0.02f, 0.9f);
        text.rectTransform.sizeDelta = new Vector2(7.5f, 3f); text.renderer.sortingOrder = 120;
        go.transform.localScale = Vector3.one * 0.38f;
        go.GetComponent<DamagePopup>().label = text;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/Combat/Damage Number.prefab");
        Object.DestroyImmediate(go); return prefab.GetComponent<DamagePopup>();
    }

    private static Material UnlitMaterial(string name, Color color)
    {
        string path = Root + "/Materials/Combat/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", color); AssetDatabase.CreateAsset(mat, path); return mat;
    }

    private static ExperienceOrb CreateXP()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "Experience Orb";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.25f;
        go.GetComponent<Renderer>().sharedMaterial = UnlitMaterial("Experience", new Color(0.15f, 1.7f, 2.3f));
        go.AddComponent<ExperienceOrb>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/Combat/Experience Orb.prefab");
        Object.DestroyImmediate(go); return prefab.GetComponent<ExperienceOrb>();
    }

    private static LineRenderer CreateLightning()
    {
        GameObject go = new GameObject("Mage Lightning", typeof(LineRenderer));
        LineRenderer line = go.GetComponent<LineRenderer>();
        line.positionCount = 6; line.widthMultiplier = 0.075f; line.useWorldSpace = true;
        line.numCapVertices = 2; line.sharedMaterial = UnlitMaterial("Lightning", new Color(0.65f, 1.6f, 2.8f));
        for (int i = 0; i < 6; i++) line.SetPosition(i, new Vector3(i * 0.4f, i % 2 == 0 ? 0 : 0.12f, 0));
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/Combat/Mage Lightning.prefab");
        Object.DestroyImmediate(go); return prefab.GetComponent<LineRenderer>();
    }

    private static Material CreateRingMaterial()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Project_/VFX/SpawnProtection.shadergraph");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Protection Shader Graph failed to import.");
        string path = Root + "/Materials/Combat/Spawn Protection.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        return mat;
    }

    private static GameObject CreateRing(Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Spawn Protection Ring";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localPosition = new Vector3(0, 0.08f, 0);
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = Vector3.one * 2.4f;
        Renderer renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/Combat/Spawn Protection Ring.prefab");
        Object.DestroyImmediate(go); return prefab;
    }

    private static MonsterUpgradeDefinition CreateMonsterDefinition(int slot, Sprite icon)
    {
        string[] names = { "Skeleton", "Mage", "Tank", "Assassin" };
        string path = Root + "/Upgrades/" + names[slot] + ".asset";
        MonsterUpgradeDefinition d = AssetDatabase.LoadAssetAtPath<MonsterUpgradeDefinition>(path);
        if (d == null)
        {
            d = ScriptableObject.CreateInstance<MonsterUpgradeDefinition>();
            AssetDatabase.CreateAsset(d, path);
        }

        d.role = (MonsterRole)slot; d.displayName = names[slot]; d.icon = icon;
        d.maxPowerLevel = 10;
        d.basePowerCost = new[] { 40, 80, 140, 120 }[slot];
        d.powerCostGrowth = new[] { 1.45f, 1.50f, 1.55f, 1.55f }[slot];
        d.healthPerLevel = new[] { 0.12f, 0.10f, 0.16f, 0.08f }[slot];
        d.damagePerLevel = new[] { 0.10f, 0.13f, 0.08f, 0.15f }[slot];

        d.maxAutoLevel = 6;
        d.baseAutoCost = new[] { 80, 160, 280, 360 }[slot];
        d.autoCostGrowth = new[] { 1.60f, 1.65f, 1.70f, 1.75f }[slot];
        d.previousMonsterAutoLevelRequired = slot == 0 ? 0 : 3;
        d.startingInterval = new[] { 18, 24, 30, 34 }[slot];
        d.intervalMultiplier = new[] { 0.75f, 0.78f, 0.80f, 0.80f }[slot];
        d.minimumInterval = new[] { 4, 6, 8, 10 }[slot];
        d.maxAlive = new[] { 18, 12, 8, 8 }[slot];
        d.chargeGoldPerAutoSummon = false;
        EditorUtility.SetDirty(d);
        return d;
    }

    private static HeroUpgradeDefinition[] CreateHeroDefinitions()
    {
        string[] names = { "Vitality", "Damage", "Attack Speed", "Move Speed", "Armor", "Pickup Range", "Cleave", "Knockback", "Life Steal", "Multi Strike", "Shockwave", "Execution" };
        string[] patterns = {
            "01100110/11111111/11111111/01111110/00111100/00011000/00000000/00000000",
            "00000011/00000110/00001100/10011000/01110000/01100000/10110000/00000000",
            "00011000/00110000/01111100/00011000/00110000/01100000/01000000/00000000",
            "00110000/00110000/00110000/00111100/01111110/11111110/00000000/00000000",
            "11111111/11000011/11000011/11000011/01100110/00111100/00011000/00000000",
            "11000011/11000011/11000011/11000011/11100111/01111110/00111100/00000000",
            "10000001/01000010/00100100/00011000/00100100/01000010/10000001/00000000",
            "00010000/00011000/11111100/11111110/11111100/00011000/00010000/00000000",
            "00011000/00111100/00111100/01111110/01111110/00111100/00011000/00000000",
            "00000101/00001010/00010100/00101000/01010000/10100000/01000000/00000000",
            "00111100/01000010/10011001/10100101/10100101/10011001/01000010/00111100",
            "00111100/01111110/11011011/11011011/01111110/00100100/00111100/00000000" };
        HeroUpgradeDefinition[] defs = new HeroUpgradeDefinition[12];
        for (int i = 0; i < 12; i++)
        {
            string path = Root + "/Upgrades/Hero " + names[i] + ".asset";
            defs[i] = AssetDatabase.LoadAssetAtPath<HeroUpgradeDefinition>(path);
            if (defs[i] != null) continue;
            Sprite icon = PixelIcon(names[i], patterns[i]);
            var d = ScriptableObject.CreateInstance<HeroUpgradeDefinition>();
            d.type = (HeroUpgradeType)i; d.displayName = names[i]; d.icon = icon;
            AssetDatabase.CreateAsset(d, path); defs[i] = d;
        }
        return defs;
    }

    private static Sprite PixelIcon(string name, string pattern)
    {
        string path = Root + "/Image/Upgrades/" + name + ".png";
        var rows = pattern.Split('/'); Texture2D tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[32 * 32];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            pixels[y * 32 + x] = rows[7 - y / 4][x / 4] == '1' ? Gold : Color.clear;
        tex.SetPixels(pixels); tex.Apply(); File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spritePixelsPerUnit = 48;
        importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static HeroUpgradeThought CreateThought(HeroUpgradeDefinition[] defs)
    {
        GameObject go = new GameObject("Hero Upgrade Thought", typeof(HeroUpgradeThought));
        var thought = go.GetComponent<HeroUpgradeThought>();
        var icon = new GameObject("Chosen upgrade icon", typeof(SpriteRenderer)); icon.transform.SetParent(go.transform, false);
        thought.upgradeIcon = icon.GetComponent<SpriteRenderer>(); thought.upgradeIcon.sprite = defs[0].icon; thought.upgradeIcon.sortingOrder = 110;
        thought.thinkingIcon = PixelIcon("Thinking", "00111100/01000010/00000010/00001100/00011000/00000000/00011000/00000000");
        var caption = new GameObject("Chosen upgrade name", typeof(TextMeshPro)); caption.transform.SetParent(go.transform, false);
        caption.transform.localPosition = new Vector3(0, -0.55f, 0); caption.transform.localScale = Vector3.one * 0.22f;
        var text = caption.GetComponent<TextMeshPro>(); text.text = "Vitality 1"; text.fontSize = 4;
        text.font = TMP_Settings.defaultFontAsset; text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(12, 2); text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = Gold; text.renderer.sortingOrder = 110; thought.caption = text;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/Combat/Hero Upgrade Thought.prefab");
        Object.DestroyImmediate(go); return prefab.GetComponent<HeroUpgradeThought>();
    }

    private static RectTransform Rect(string name, Transform parent, float left, float top, float right, float height)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
        var rt = go.GetComponent<RectTransform>(); rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(left, -top - height); rt.offsetMax = new Vector2(-right, -top); return rt;
    }
    private static TMP_Text Text(string name, Transform parent, string value, float left, float top, float right, float height, float size, Color color)
    {
        RectTransform rt = Rect(name, parent, left, top, right, height);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>(); text.font = TMP_Settings.defaultFontAsset;
        text.text = value; text.fontSize = size; text.color = color; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.MidlineLeft; return text;
    }
    private static Image Image(string name, Transform parent, Color color, float left, float top, float right, float height)
    {
        var image = Rect(name, parent, left, top, right, height).gameObject.AddComponent<Image>(); image.color = color; return image;
    }
    private static Button Button(string name, Transform parent, float top, Color color, out TMP_Text label)
    {
        Image image = Image(name, parent, color, 16, top, 16, 36);
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        ColorBlock colors = button.colors; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f); colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f); button.colors = colors;
        label = Text("Label", image.transform, name, 12, 0, 12, 36, 18, new Color(0.96f, 0.97f, 0.91f));
        return button;
    }
    private static void CreatePanel(GameObject panel, SummonerUpgrades upgrades)
    {
        var root = new GameObject("Summoner Upgrades", typeof(RectTransform)); root.layer = 5;
        var rect = root.GetComponent<RectTransform>(); rect.SetParent(panel.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        root.AddComponent<Image>().color = Ink;
        Text("Heading", root.transform, "SUMMONER'S GRIMOIRE", 28, 22, 20, 38, 27, Gold);
        Text("Subtitle", root.transform, "Strengthen your army. Automate summons.", 28, 60, 16, 26, 17, Mint);
        upgrades.armyCountLabel = Text("Army Capacity", root.transform, "ARMY  0 / 36", 28, 93, 20, 26, 18, Color.white);
        Image("Header Divider", root.transform, Mint * new Color(1,1,1,0.3f), 28, 126, 28, 2);
        for (int i = 0; i < 4; i++)
        {
            var d = upgrades.definitions[i];
            Image card = Image(d.displayName + " Upgrade Card", root.transform, Card, 24, 144 + i * 210, 24, 196);
            var controller = card.gameObject.AddComponent<MonsterUpgradeCard>(); controller.upgrades = upgrades; controller.slot = i;
            Image icon = Image("Monster Icon", card.transform, Color.white, 16, 12, 0, 38);
            icon.rectTransform.anchorMax = new Vector2(0,1); icon.rectTransform.sizeDelta = new Vector2(38,38);
            icon.sprite = d.icon; icon.preserveAspect = true; controller.icon = icon;
            controller.title = Text("Monster Name", card.transform, d.displayName, 66, 9, 12, 28, 23, Gold);
            controller.stats = Text("Power Bonuses", card.transform, "SUMMON 0 G   FIELD 0/0\nHP +0%   ATK +0%", 66, 34, 12, 30, 14, Mint);
            controller.powerButton = Button("Upgrade Power", card.transform, 68, new Color(0.3f, 0.23f, 0.12f), out TMP_Text power);
            controller.powerLabel = power;
            controller.autoButton = Button("Upgrade Automation", card.transform, 111, new Color(0.12f, 0.28f, 0.25f), out TMP_Text auto);
            controller.autoLabel = auto;
            controller.autoStatus = Text("Automation Status", card.transform, "", 16, 152, 12, 26, 15, new Color(0.72f,0.79f,0.78f));
            Image track = Image("Timer Track", card.transform, Ink, 16, 185, 16, 3);
            Image progress = Image("Timer Fill", track.transform, Mint, 0, 0, 0, 3);
            progress.type = UnityEngine.UI.Image.Type.Filled; progress.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            progress.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); progress.fillAmount = 0; controller.progress = progress;
            controller.Refresh();
        }
        Text("Automation Help", root.transform, "AUTO unlock chain: previous monster AUTO Lv.3.\nAUTO caps at Lv.6. Global army cap: 36. Timed summons cost no extra gold.", 28, 990, 20, 64, 16, Mint).textWrappingMode = TextWrappingModes.Normal;
        // Save the exact layout as a reusable prefab; scene bindings remain on the actual scene objects.
        PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Combat/Summoner Upgrade Panel.prefab");
    }

    [MenuItem("Hero Doom/Run Combat Checks")]
    public static void RunChecks()
    {
        Validate();

        PlayerSpawner spawner = Object.FindFirstObjectByType<PlayerSpawner>();
        SummonerUpgrades upgrades = spawner != null ? spawner.upgrades : null;
        if (upgrades == null) throw new Exception("Upgrade system is not wired.");

        for (int i = 0; i < upgrades.definitions.Length; i++)
        {
            MonsterUpgradeDefinition d = upgrades.definitions[i];
            if (d == null) throw new Exception("Missing monster definition: " + i);

            double previousPowerCost = 0d;
            for (int level = 0; level < d.maxPowerLevel; level++)
            {
                double cost = d.PowerCost(level);
                if (cost <= previousPowerCost) throw new Exception("Power costs must increase: " + d.displayName);
                previousPowerCost = cost;
            }

            double previousAutoCost = 0d;
            float previousInterval = float.MaxValue;
            for (int level = 1; level <= d.maxAutoLevel; level++)
            {
                double cost = d.AutoCost(level - 1);
                float interval = d.Interval(level);
                if (cost <= previousAutoCost) throw new Exception("AUTO costs must increase: " + d.displayName);
                if (interval > previousInterval + 0.001f) throw new Exception("AUTO interval must not get slower: " + d.displayName);
                if (interval < d.minimumInterval - 0.001f) throw new Exception("AUTO interval below minimum: " + d.displayName);
                previousAutoCost = cost;
                previousInterval = interval;
            }
        }

        File.AppendAllText(Work + "/result.txt", "Progression cost/interval checks OK\n");
    }

    [MenuItem("Hero Doom/Validate Combat Setup")]
    public static void Validate()
    {
        string report = "Combat setup validation\n";
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Project_/VFX/SpawnProtection.shadergraph");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Ring shader has errors.");
        var spawner = Object.FindFirstObjectByType<PlayerSpawner>();
        if (spawner == null || spawner.upgrades == null) throw new Exception("Upgrade system is not wired.");
        for (int i = 0; i < 4; i++)
        {
            var m = spawner.GetPrefab(i).GetComponent<MonsterAI>();
            if (m.damagePopupPrefab == null || m.experienceOrbPrefab == null || m.spawnProtection == null || m.knockbackMotor == null) throw new Exception("Monster feedback missing: " + i);
            if (m.GetComponentInChildren<MonsterHealthBar>(true) == null) throw new Exception("Monster health bar missing: " + i);
            if (m.GetComponent<NavMeshAgent>() == null || !m.GetComponent<Rigidbody>().isKinematic) throw new Exception("Physics missing: " + i);
            if (i == 1 && (m.mageProjectilePrefab == null || m.mageProjectilePrefab.GetComponent<MagicProjectile>() == null || m.lightningPrefab == null)) throw new Exception("Mage effects missing.");
            report += m.name + ": feedback, protection, navigation OK\n";
        }
        var cards = Object.FindObjectsByType<MonsterUpgradeCard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (cards.Length != 4 || cards.Any(c => c.upgrades == null || c.powerButton == null || c.autoButton == null)) throw new Exception("Expected four authored cards with bindings.");

        SummonerUpgrades upgrades = spawner.upgrades;
        if (upgrades.maxAliveMonsters != 36) throw new Exception("Global monster cap should be 36.");
        int[] expectedCaps = { 18, 12, 8, 8 };
        float[] expectedMinimumIntervals = { 4f, 6f, 8f, 10f };
        for (int i = 0; i < 4; i++)
        {
            MonsterUpgradeDefinition d = upgrades.definitions[i];
            if (d == null) throw new Exception("Missing upgrade definition: " + i);
            if (d.maxAutoLevel != 6) throw new Exception("AUTO max level should be 6: " + d.displayName);
            if (d.maxAlive != expectedCaps[i]) throw new Exception("Wrong per-monster cap: " + d.displayName);
            if (Mathf.Abs(d.minimumInterval - expectedMinimumIntervals[i]) > 0.01f) throw new Exception("Wrong minimum interval: " + d.displayName);
            if (i > 0 && d.previousMonsterAutoLevelRequired != 3) throw new Exception("AUTO dependency should require previous Lv.3: " + d.displayName);
        }

        report += "4 authored upgrade cards OK\nSummoner caps and AUTO progression OK\nShader Graph: no shader errors\n";
        File.WriteAllText(Work + "/result.txt", report);
    }
}
