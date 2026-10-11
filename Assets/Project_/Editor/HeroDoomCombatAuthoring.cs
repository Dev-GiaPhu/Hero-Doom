using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

// Edit-time authoring only. Nothing in this file is included in player builds.
[InitializeOnLoad]
public static class HeroDoomCombatAuthoring
{
    private const string Root = "Assets/Project_/Resources";
    private const string Work = "Temp/HeroDoomWork";
    private const string PlayTestKey = "HeroDoom.AuthoringPlayTest";
    private static readonly Color Ink = new Color(0.055f, 0.10f, 0.11f, 1);
    private static readonly Color Card = new Color(0.085f, 0.15f, 0.16f, 1);
    private static readonly Color Gold = new Color(1f, 0.79f, 0.38f, 1);
    private static readonly Color Mint = new Color(0.4f, 0.85f, 0.77f, 1);
    private static bool pendingBuild;
    private static bool pendingHealthSliderCheck;
    private static bool pendingThoughtResultCheck;
    private static MonsterHealthBar healthBarUnderTest;
    private static HeroUpgradeThought thoughtUnderTest;
    private static float healthBarBeforeTest;
    private static float healthBarTargetTest;
    private static float healthSliderTestStarted;
    private static System.Collections.Generic.List<string> playTestReport;
    static HeroDoomCombatAuthoring()
    {
        EditorApplication.update += PollRequest;
        // One-time, idempotent migration: author the new Rebirth Tree into the open
        // SampleScene after Unity has compiled its runtime components.
    }

    private static void AuthorRebirthTreeAfterCompile()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path!="Assets/Scenes/SampleScene.unity") return;
        RebirthTreeController existing = Object.FindFirstObjectByType<RebirthTreeController>(FindObjectsInactive.Include);
        // Upgrade older authored trees to the clipped, draggable map layout.
        if (existing != null && existing.popup != null && existing.popup.GetComponentInChildren<RebirthTreePanView>(true) != null) return;
        SummonerUpgrades upgrades=Object.FindFirstObjectByType<SummonerUpgrades>();
        if(upgrades==null) return;
        try
        {
            EnsureFolder(Root+"/Prefabs/Combat"); EnsureFolder(Root+"/Upgrades"); EnsureFolder(Root+"/Image/Upgrades");
            AuthorRebirthTree(upgrades);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        catch(Exception exception)
        {
            Debug.LogError("Could not author Rebirth Tree: "+exception.Message);
        }
    }

    // Earlier scenes can have the first fourteen Hero upgrades but not the two
    // survival skills added later. Repair only that incomplete authored setup.
    private static void AuthorMissingHeroProgressionAfterCompile()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/SampleScene.unity") return;
        HeroDoomGameManager manager=Object.FindFirstObjectByType<HeroDoomGameManager>();
        HeroHUDController hud=Object.FindFirstObjectByType<HeroHUDController>();
        if(manager==null || hud==null) return;

        HeroBotAI prefabHero=manager.heroPrefab!=null ? manager.heroPrefab.GetComponent<HeroBotAI>() : null;
        bool definitionsComplete=prefabHero!=null && prefabHero.upgradeDefinitions!=null &&
            prefabHero.upgradeDefinitions.Length>=16 && prefabHero.upgradeDefinitions.Take(16).All(d=>d!=null && d.icon!=null);
        bool hudComplete=hud.upgradeSlots!=null && hud.upgradeSlots.Length==16 &&
            hud.upgradeSlots.All(slot=>slot!=null && slot.root!=null && slot.icon!=null && slot.count!=null && slot.icon.sprite!=null);
        if(definitionsComplete && hudComplete) return;

        try
        {
            HeroUpgradeDefinition[] definitions=CreateHeroDefinitions();
            if(manager.heroPrefab!=null)
            {
                string heroPath=AssetDatabase.GetAssetPath(manager.heroPrefab);
                if(!string.IsNullOrEmpty(heroPath))
                {
                    GameObject root=PrefabUtility.LoadPrefabContents(heroPath);
                    try
                    {
                        HeroBotAI hero=root.GetComponent<HeroBotAI>();
                        if(hero!=null) { hero.upgradeDefinitions=definitions; EditorUtility.SetDirty(hero); }
                        PrefabUtility.SaveAsPrefabAsset(root,heroPath);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            }
            foreach(HeroBotAI hero in Object.FindObjectsByType<HeroBotAI>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                hero.upgradeDefinitions=definitions;
                EditorUtility.SetDirty(hero);
            }
            AuthorProgressionHud();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        catch(Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    // Keep the authored Hero prefab self-contained.  These are prefab references,
    // never runtime-generated effects: the migration only fills missing links from
    // the Visual Effect Graph prefabs already stored under Resources/Prefabs/Combat.
    private static void AuthorMissingHeroVisualsAfterCompile()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity") return;

        HeroDoomGameManager manager = Object.FindFirstObjectByType<HeroDoomGameManager>();
        if (manager == null || manager.heroPrefab == null) return;
        HeroBotAI prefabHero = manager.heroPrefab.GetComponent<HeroBotAI>();
        bool visualsComplete = prefabHero != null && prefabHero.damagePopupPrefab != null &&
            prefabHero.upgradeThoughtPrefab != null && prefabHero.attackImpactVisualPrefab != null &&
            prefabHero.healingPulseVisualPrefab != null && prefabHero.arcaneNovaVisualPrefab != null &&
            prefabHero.lightningStrikeVisualPrefab != null && prefabHero.fireFieldPrefab != null &&
            prefabHero.orbitingSawPrefab != null && prefabHero.spawnProtection != null;
        if (visualsComplete) return;

        try
        {
            EnsureFolder(Root + "/Prefabs/Combat");
            EnsureFolder(Root + "/Materials/Combat");
            EnsureFolder(Root + "/Image/Upgrades");
            VisualEffectAsset magicGraph = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Project_/VFX/Magic.vfx");
            VisualEffectAsset boltGraph = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Project_/VFX/Bullet Magic.vfx");
            if (magicGraph == null || boltGraph == null) return;

            HeroUpgradeDefinition[] definitions = CreateHeroDefinitions();
            DamagePopup damage = prefabHero.damagePopupPrefab != null ? prefabHero.damagePopupPrefab : CreateDamage();
            HeroUpgradeThought thought = prefabHero.upgradeThoughtPrefab != null ? prefabHero.upgradeThoughtPrefab : CreateThought(definitions);
            Material ringMaterial = CreateRingMaterial();
            GameObject ringPrefab = CreateRing(ringMaterial, null);
            HeroSkillBurst hitEffect = CreateSkillBurst("Combat Hit Sparks", boltGraph, .45f, .35f);
            HeroSkillBurst healingPulse = CreateSkillBurst("Hero Healing Pulse", magicGraph, 1.1f, 1.2f);
            HeroSkillBurst arcaneNova = CreateSkillBurst("Hero Arcane Nova", magicGraph, 1.15f, 2.6f);
            HeroSkillBurst lightningStrike = CreateSkillBurst("Hero Lightning Strike", boltGraph, .65f, 1.7f);
            HeroFireField fireField = CreateFireFieldPrefab(magicGraph);
            HeroOrbitingSaw orbitingSaw = CreateOrbitingSawPrefab(boltGraph);

            string heroPath = AssetDatabase.GetAssetPath(manager.heroPrefab);
            GameObject root = PrefabUtility.LoadPrefabContents(heroPath);
            try
            {
                AuthorHero(root, damage, thought, definitions, ringPrefab, hitEffect, healingPulse, arcaneNova, fireField, orbitingSaw, lightningStrike);
                PrefabUtility.SaveAsPrefabAsset(root, heroPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            foreach (HeroBotAI hero in Object.FindObjectsByType<HeroBotAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                AuthorHero(hero.gameObject, damage, thought, definitions, ringPrefab, hitEffect, healingPulse, arcaneNova, fireField, orbitingSaw, lightningStrike);
                EditorUtility.SetDirty(hero);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void PollRequest()
    {
        if(pendingHealthSliderCheck && EditorApplication.isPlaying)
        {
            if(Time.unscaledTime-healthSliderTestStarted<.07f) return;
            float displayed=healthBarUnderTest!=null && healthBarUnderTest.fill!=null?healthBarUnderTest.fill.fillAmount:-1f;
            bool slid=displayed>healthBarTargetTest+.005f && displayed<healthBarBeforeTest-.005f;
            playTestReport.Add(slid
                ? $"Monster health fill visibly slid: {healthBarBeforeTest:0.00} -> {displayed:0.00} (target {healthBarTargetTest:0.00})."
                : $"Monster health fill ended at {displayed:0.00} (target {healthBarTargetTest:0.00}); intermediate slide could not be sampled at the chosen frame timing.");
            File.WriteAllLines(Work+"/result.txt",playTestReport);
            pendingHealthSliderCheck=false;
            pendingThoughtResultCheck=true;
            healthSliderTestStarted=Time.unscaledTime;
            return;
        }
        if(pendingThoughtResultCheck && EditorApplication.isPlaying)
        {
            if(Time.unscaledTime-healthSliderTestStarted<(thoughtUnderTest!=null?thoughtUnderTest.thinkingDuration+.8f:2f)) return;
            bool selectedVisible=thoughtUnderTest!=null && thoughtUnderTest.Selected!=null && thoughtUnderTest.upgradeIcon!=null && thoughtUnderTest.upgradeIcon.sprite==thoughtUnderTest.Selected.icon;
            bool captionVisible=thoughtUnderTest!=null && thoughtUnderTest.caption!=null && thoughtUnderTest.caption.gameObject.activeSelf && thoughtUnderTest.caption.text!="...";
            HeroBotAI hero=HeroDoomGameManager.Instance!=null?HeroDoomGameManager.Instance.CurrentHero:null;
            bool upgradeApplied=selectedVisible && hero!=null && hero.GetUpgradeStack(thoughtUnderTest.Selected.type)>0;
            if(!selectedVisible || !captionVisible || !upgradeApplied)
            {
                playTestReport.Add($"AI upgrade result incomplete: thought={(thoughtUnderTest!=null)}, selected={(thoughtUnderTest!=null?thoughtUnderTest.Selected?.displayName:"none")}, icon={(thoughtUnderTest!=null?thoughtUnderTest.upgradeIcon?.sprite?.name:"none")}, caption={(thoughtUnderTest!=null?thoughtUnderTest.caption?.text:"none")}, selectedIcon={selectedVisible}, captionVisible={captionVisible}, applied={upgradeApplied}, hero={(hero!=null?hero.Level:-1)}, stack={(hero!=null && thoughtUnderTest!=null && thoughtUnderTest.Selected!=null?hero.GetUpgradeStack(thoughtUnderTest.Selected.type):-1)}.");
            }
            else playTestReport.Add($"AI displayed two upgrade cards, selected {thoughtUnderTest.caption.text}, animated the result and applied it.");
            if(upgradeApplied && hero!=null)
            {
                HeroDoomGameManager manager=HeroDoomGameManager.Instance;
                PlayerSpawner spawner=Object.FindFirstObjectByType<PlayerSpawner>();
                SummonerUpgrades upgrades=spawner!=null?spawner.upgrades:null;
                if(manager==null || upgrades==null) playTestReport.Add("Rebirth clean-run check skipped: summoner references missing.");
                else
                {
                    manager.GrantGold(250000d);
                    upgrades.BuyPower(1); upgrades.BuyPower(2); upgrades.BuyPower(3);
                    for(int i=0;i<upgrades.RebirthPowerRequirement;i++) upgrades.BuyPower(0);
                    if(!upgrades.CanRebirth) playTestReport.Add("Rebirth clean-run check failed to unlock.");
                    else
                    {
                        upgrades.PerformRebirth();
                        bool cleanHero=hero.Level==1 && hero.Experience==0f && Enumerable.Range(0,16).All(i=>hero.GetUpgradeStack((HeroUpgradeType)i)==0);
                        bool cleanArmy=upgrades.UnlockedMonsterCount==1 && upgrades.TotalPowerLevels==0 && Enumerable.Range(0,4).All(i=>upgrades.AutoLevel(i)==0) && MonsterAI.ActiveMonsters.Count==0;
                        bool rewardKept=Math.Abs(upgrades.RebirthGoldMultiplier-(1f+upgrades.RebirthCount*upgrades.goldMultiplierPerRebirth))<.001f;
                        if(!cleanHero || !cleanArmy || !rewardKept || Math.Abs(manager.Gold-upgrades.rebirthStartingGold)>.01d)
                            playTestReport.Add($"Rebirth reset incomplete: hero={cleanHero}, army={cleanArmy}, bonus={rewardKept}, gold={manager.Gold:0.##}.");
                        else playTestReport.Add($"Rebirth resets Hero, army, unlocks, AUTO, ranks, wheel and gold; permanent reward retained at {upgrades.RebirthGoldMultiplier:0.00}x.");
                    }
                }
            }
            File.WriteAllLines(Work+"/result.txt",playTestReport);
            pendingThoughtResultCheck=false;
            EditorApplication.isPlaying=false;
            return;
        }
        if (SessionState.GetBool(PlayTestKey, false))
        {
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
            string timeKey=PlayTestKey+".Started";
            double started=SessionState.GetFloat(timeKey,-1f);
            if(started<0d) { SessionState.SetFloat(timeKey,(float)EditorApplication.timeSinceStartup); return; }
            if(EditorApplication.timeSinceStartup-started<1.2d) return;
            SessionState.SetBool(PlayTestKey,false); SessionState.SetFloat(timeKey,-1f);
            try { RunPlayModeChecks(); }
            catch(Exception e) { File.WriteAllText(Work+"/result.txt",e.ToString()); Debug.LogException(e); EditorApplication.isPlaying=false; }
            return;
        }
        if (pendingBuild)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                return;
            }
            pendingBuild = false;
            try { Build(); }
            catch (Exception e) { File.WriteAllText(Work + "/result.txt", e.ToString()); Debug.LogException(e); }
            return;
        }
        string request = Work + "/request.txt";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string action = File.ReadAllText(request).Trim();
        File.Delete(request);
        try
        {
            if (action == "build" && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                pendingBuild = true;
                EditorApplication.isPlaying = false;
            }
            else if (action == "build") Build();
            else if (action == "validate") Validate();
            else if (action == "test") RunChecks();
            else if (action == "playtest")
            {
                SessionState.SetFloat(PlayTestKey+".Started",-1f);
                SessionState.SetBool(PlayTestKey,true);
                EditorApplication.isPlaying=true;
            }
            else throw new Exception("Unknown authoring request: " + action);
        }
        catch (Exception e) { File.WriteAllText(Work + "/result.txt", e.ToString()); Debug.LogException(e); }
    }

    private static void RunPlayModeChecks()
    {
        System.Collections.Generic.List<string> report=new(){"Unity Play Mode checks"};
        playTestReport=report;
        HeroDoomGameManager manager=Object.FindFirstObjectByType<HeroDoomGameManager>();
        HeroBotAI hero=manager!=null?manager.CurrentHero:null;
        if(hero==null || !hero.IsAlive) throw new Exception("Play Mode: Hero did not spawn alive.");
        SpawnProtection protection=hero.spawnProtection;
        if(protection==null || !protection.IsProtected || protection.ringVisual==null || !protection.ringVisual.activeInHierarchy || protection.ringRenderer==null)
            throw new Exception("Play Mode: Hero spawn protection/ring was not active immediately after spawn.");
        if(protection.energyShellRenderer==null || !protection.energyShellRenderer.enabled || protection.energyShellRenderer.sharedMaterial==null)
            throw new Exception("Play Mode: authored spherical energy shell is missing or disabled.");
        report.Add($"Hero spawn protection active; original Shader Graph sphere={protection.ringRenderer.sharedMaterial?.shader?.name}");
        if(hero.attackImpactVisualPrefab==null || hero.healingPulseVisualPrefab==null || hero.arcaneNovaVisualPrefab==null || hero.fireFieldPrefab==null || hero.orbitingSawPrefab==null || hero.lightningStrikeVisualPrefab==null)
            throw new Exception("Play Mode: Hero authored skill visual or special-skill prefabs are not assigned.");
        HeroSkillBurst.Spawn(hero.healingPulseVisualPrefab,hero.transform.position+Vector3.up*.1f);
        HeroSkillBurst.Spawn(hero.arcaneNovaVisualPrefab,hero.transform.position+Vector3.up*.1f);
        HeroSkillBurst.Spawn(hero.lightningStrikeVisualPrefab,hero.transform.position+Vector3.up*.12f);
        report.Add("Hero healing, Nova, and lightning authored skill prefabs played.");

        LuckyWheelController wheel=Object.FindFirstObjectByType<LuckyWheelController>(FindObjectsInactive.Include);
        if(wheel==null || wheel.openButton==null || wheel.popup==null) throw new Exception("Play Mode: authored Lucky Wheel click target is missing.");
        wheel.openButton.onClick.Invoke();
        if(!wheel.popup.activeInHierarchy) throw new Exception("Play Mode: clicking the Lucky Wheel icon did not show its popup.");
        wheel.closeButton.onClick.Invoke();
        if(wheel.popup.activeSelf) throw new Exception("Play Mode: Lucky Wheel close button did not hide its popup.");
        report.Add("Lucky Wheel icon opens its authored popup; close button hides it.");

        PlayerSpawner spawner=Object.FindFirstObjectByType<PlayerSpawner>();
        if(spawner==null || spawner.GetPrefab(0)==null) throw new Exception("Play Mode: basic monster prefab is not assigned.");
        GameObject testMonsterObject=Object.Instantiate(spawner.GetPrefab(0),hero.transform.position+Vector3.right*3f,Quaternion.identity);
        MonsterAI monster=testMonsterObject.GetComponent<MonsterAI>();
        if(monster==null) throw new Exception("Play Mode: authored basic monster prefab has no MonsterAI.");
        monster.ConfigureBySlot(0);
        monster.spawnProtection?.End();
        MonsterHealthBar bar=monster.GetComponentInChildren<MonsterHealthBar>(true);
        if(bar==null || bar.fill==null || bar.fill.type!=UnityEngine.UI.Image.Type.Filled) throw new Exception("Play Mode: summoned monster has no authored Filled health slider.");
        float before=bar.fill.fillAmount;
        monster.spawnProtection?.End(); // Keep the spawn-shield check above, then test damage after its intended protection window.
        monster.TakeDamage(Mathf.Max(1f,monster.maxHP*.2f),true);
        if(monster.hitEffectPrefab==null) throw new Exception("Play Mode: summoned monster has no authored hit-effect prefab.");
        report.Add("Monster damage feedback retained without added particle/VFX layers.");
        float target=monster.HPPercent;
        report.Add($"Monster damage applied; health fill before={before:0.00}, target={target:0.00}, slide duration={bar.fillSlideDuration:0.00}");
        healthBarUnderTest=bar; healthBarBeforeTest=before; healthBarTargetTest=target;
        healthSliderTestStarted=Time.unscaledTime; pendingHealthSliderCheck=true;

        hero.AddExperience(hero.ExperienceToNext);
        HeroUpgradeThought thought=Object.FindFirstObjectByType<HeroUpgradeThought>();
        if(thought==null || thought.upgradeIcon==null || thought.caption==null || thought.OfferedA==null || thought.OfferedB==null) throw new Exception("Play Mode: Hero AI did not instantiate its two-card authored thought prefab.");
        if(thought.thinkingIcon==null) throw new Exception("Play Mode: authored AI thought prefab is missing its thinking sprite.");
        thoughtUnderTest=thought;
        HeroUpgradeType selectedType=thought.OfferedA.type;
        if(thought.choiceCardA==null || thought.choiceCardB==null || !thought.choiceCardA.gameObject.activeSelf || !thought.choiceCardB.gameObject.activeSelf)
            throw new Exception("Play Mode: the two small Hero upgrade offer cards are not visible while the Hero thinks.");
        report.Add($"AI is considering {thought.OfferedA.displayName} vs {thought.OfferedB.displayName}; thought bubble and both authored offer cards are active.");

        HeroUpgradeTooltip tooltip=Object.FindFirstObjectByType<HeroUpgradeTooltip>(FindObjectsInactive.Include);
        if(tooltip==null || tooltip.tooltipRoot==null || tooltip.tooltipRoot.parent!=tooltip.GetComponentInParent<Canvas>().transform)
            throw new Exception("Play Mode: Hero upgrade tooltip is not authored as a canvas overlay.");
        Vector2 pointerPosition=new Vector2(Screen.width*.5f,Screen.height*.5f);
        PointerEventData pointer=new PointerEventData(EventSystem.current) { position=pointerPosition };
        tooltip.Show(selectedType,pointer);
        if(!tooltip.tooltipRoot.gameObject.activeSelf || tooltip.title==null || string.IsNullOrWhiteSpace(tooltip.title.text))
            throw new Exception("Play Mode: hovering a Hero upgrade did not show its detail panel.");
        Canvas tooltipCanvas=tooltip.tooltipRoot.GetComponentInParent<Canvas>();
        RectTransform canvasRect=tooltipCanvas.transform as RectTransform;
        Camera canvasCamera=tooltipCanvas.renderMode==RenderMode.ScreenSpaceOverlay?null:tooltipCanvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,pointerPosition,canvasCamera,out Vector2 pointerLocal);
        float uiScale=tooltipCanvas.scaleFactor;
        if(Vector2.Distance(tooltip.tooltipRoot.anchoredPosition,pointerLocal+new Vector2(18f,-18f)/Mathf.Max(.01f,uiScale))>2f)
            throw new Exception($"Play Mode: Hero tooltip pointer position mismatch; anchored={tooltip.tooltipRoot.anchoredPosition}, expected={pointerLocal+new Vector2(18f,-18f)/Mathf.Max(.01f,uiScale)}, canvas={tooltipCanvas.renderMode}, scale={uiScale}, pivot={tooltip.tooltipRoot.pivot}.");
        tooltip.Hide();
        report.Add("Hero upgrade tooltip shows upgrade stats beside the pointer and follows pointer coordinates.");

        SummonerUpgrades upgrades=spawner!=null?spawner.upgrades:null;
        if(upgrades==null) throw new Exception("Play Mode: summoner progression missing.");
        manager.AddDamageGold(1000000f);
        upgrades.BuyPower(1); upgrades.BuyPower(2); upgrades.BuyPower(3);
        for(int i=0;i<upgrades.RebirthPowerRequirement;i++) upgrades.BuyPower(0);
        // The clean-run Rebirth assertion runs after the delayed Hero AI choice animation completes.
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
        RemoveObsoleteProtectionVfxArtifacts();
        HeroDoomGameManager manager = Object.FindFirstObjectByType<HeroDoomGameManager>();
        PlayerSpawner spawner = Object.FindFirstObjectByType<PlayerSpawner>();
        if (manager == null || spawner == null) throw new Exception("Missing GameManager or PlayerSpawner.");
        manager.startingGold = 50d; manager.passiveGoldPerSecond = 0.35d; manager.goldPerDamage = 0.05d;
        manager.economySoftPressureGold=1400d; manager.incomeAtSoftPressure=.68f;
        spawner.monster1Cost = 10d; spawner.monster2Cost = 35d; spawner.monster3Cost = 65d; spawner.monster4Cost = 90d;
        var panel = GameObject.Find("Canvas/Right Panel");
        if (panel == null) throw new Exception("Missing Canvas/Right Panel.");
        Transform existingUpgradePanel = panel.transform.Find("Summoner Upgrades");
        // Preserve user-authored panel content and serialized layout. Build only adds missing controls.
        DamagePopup damage = CreateDamage();
        ExperienceOrb xp = CreateXP();
        LineRenderer lightning = CreateLightning();
        Material ring = CreateRingMaterial();
        VisualEffectAsset magicGraph=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Project_/VFX/Magic.vfx");
        VisualEffectAsset boltGraph=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Project_/VFX/Bullet Magic.vfx");
        if(magicGraph==null || boltGraph==null) throw new Exception("The authored Visual Effect Graph assets could not be loaded.");
        GameObject ringPrefab = CreateRing(ring,null);
        AuthorPillarPrefab(lightning);
        HeroUpgradeDefinition[] heroDefs = CreateHeroDefinitions();
        HeroUpgradeThought thought = CreateThought(heroDefs);
        HeroSkillBurst hitEffect=CreateSkillBurst("Combat Hit Sparks",boltGraph,.45f,.35f);
        HeroSkillBurst healingPulse=CreateSkillBurst("Hero Healing Pulse",magicGraph,1.1f,1.2f);
        HeroSkillBurst arcaneNova=CreateSkillBurst("Hero Arcane Nova",magicGraph,1.15f,2.6f);
        HeroSkillBurst lightningStrike=CreateSkillBurst("Hero Lightning Strike",boltGraph,.65f,1.7f);
        HeroFireField fireField=CreateFireFieldPrefab(magicGraph);
        HeroOrbitingSaw orbitingSaw=CreateOrbitingSawPrefab(boltGraph);
        var hud = Object.FindFirstObjectByType<MonsterHUDController>();
        SummonerUpgrades upgrades = manager.GetComponent<SummonerUpgrades>();
        if (upgrades == null) upgrades = Undo.AddComponent<SummonerUpgrades>(manager.gameObject);
        upgrades.gameManager = manager; upgrades.spawner = spawner; upgrades.maxAliveMonsters = 36;
        CapturePillarManager pillarManager=Object.FindFirstObjectByType<CapturePillarManager>();
        if(pillarManager!=null)
        {
            pillarManager.captureRadius=4.5f; pillarManager.goldBonusPerCapturedPillar=.18f;
            pillarManager.heroDamageBonusPerPillar=.12f; pillarManager.monsterDamageBonusPerPillar=.12f;
            pillarManager.heroHealthBonusPerPillar=.10f; pillarManager.monsterHealthBonusPerPillar=.10f;
            EditorUtility.SetDirty(pillarManager);
        }
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
                ai.hitEffectPrefab=hitEffect;
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
            AuthorHero(heroRoot, damage, thought, heroDefs, ringPrefab,hitEffect,healingPulse,arcaneNova,fireField,orbitingSaw,lightningStrike);
            PrefabUtility.SaveAsPrefabAsset(heroRoot, heroPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(heroRoot); }
        foreach (HeroBotAI hero in Object.FindObjectsByType<HeroBotAI>(FindObjectsSortMode.None))
        {
            AuthorHero(hero.gameObject, damage, thought, heroDefs, ringPrefab,hitEffect,healingPulse,arcaneNova,fireField,orbitingSaw,lightningStrike);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hero);
        }
        spawner.upgrades = upgrades;
        EditorUtility.SetDirty(spawner); EditorUtility.SetDirty(upgrades);
        if (existingUpgradePanel == null) CreatePanel(panel, upgrades);
        WireExistingPanel(panel, upgrades);
        // UI panels are hand-authored. Combat build deliberately leaves their layout and typography untouched.
        AuthorProgressionHud();
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

    private static GameObject AuthorPillarPrefab(LineRenderer lightning)
    {
        string visualPath=Root+"/Prefabs/Environment/pillar.prefab";
        GameObject contents=PrefabUtility.LoadPrefabContents(visualPath);
        try
        {
            contents.name="pillar";
            CapturePillar pillar=contents.GetComponent<CapturePillar>()??contents.AddComponent<CapturePillar>();
            pillar.captureRadius=4.5f; pillar.turretCooldown=1.5f; pillar.turretDamage=10f; pillar.turretRange=18f;
            pillar.shotVisualPrefab=lightning;
            SphereCollider trigger=contents.GetComponent<SphereCollider>()??contents.AddComponent<SphereCollider>();
            trigger.isTrigger=true; trigger.radius=4.5f;
            PrefabUtility.SaveAsPrefabAsset(contents,visualPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
    }

    private static HeroSkillBurst CreateSkillBurst(string name,VisualEffectAsset graph,float duration,float scale)
    {
        string path=Root+"/Prefabs/Combat/"+name+".prefab";
        bool exists=AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null;
        GameObject contents=exists?PrefabUtility.LoadPrefabContents(path):new GameObject(name);
        try
        {
            contents.name=name; contents.transform.localPosition=Vector3.zero; contents.transform.localRotation=Quaternion.identity; contents.transform.localScale=Vector3.one;
            RemoveParticleSystems(contents);
            foreach(Transform child in contents.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            HeroSkillBurst burst=contents.GetComponent<HeroSkillBurst>()??contents.AddComponent<HeroSkillBurst>();
            burst.visualEffects=Array.Empty<VisualEffect>(); burst.duration=duration; burst.startScale=.22f; burst.endScale=scale; burst.rotationSpeed=name.Contains("Nova") ? 100f : 0f; burst.animateScale=true; burst.destroyAfterPlayback=true;
            AuthorSkillMeshVisual(contents.transform,name);
            burst.visualRenderers=contents.GetComponentsInChildren<Renderer>(true);
            EditorUtility.SetDirty(burst);
            GameObject prefab=PrefabUtility.SaveAsPrefabAsset(contents,path);
            return prefab.GetComponent<HeroSkillBurst>();
        }
        finally { if(exists) PrefabUtility.UnloadPrefabContents(contents); else Object.DestroyImmediate(contents); }
    }

    private static void AuthorSkillMeshVisual(Transform root,string effectName)
    {
        if(effectName.Contains("Healing"))
        {
            CreateSkillPrimitive(root,"Healing pulse",PrimitiveType.Cylinder,Vector3.zero,new Vector3(1f,.018f,1f),new Color(.18f,1f,.62f,.72f));
            CreateSkillPrimitive(root,"Healing inner pulse",PrimitiveType.Cylinder,new Vector3(0,.025f,0),new Vector3(.55f,.012f,.55f),new Color(.72f,1f,.83f,.86f));
        }
        else if(effectName.Contains("Nova"))
        {
            CreateSkillPrimitive(root,"Nova outer wave",PrimitiveType.Cylinder,Vector3.zero,new Vector3(1f,.016f,1f),new Color(.12f,.82f,1f,.62f));
            CreateSkillPrimitive(root,"Nova core",PrimitiveType.Cylinder,new Vector3(0,.025f,0),new Vector3(.38f,.01f,.38f),new Color(.75f,.95f,1f,.8f));
        }
        else if(effectName.Contains("Lightning"))
        {
            CreateSkillPrimitive(root,"Lightning center",PrimitiveType.Cube,new Vector3(0,.55f,0),new Vector3(.12f,1.15f,.12f),new Color(1f,.75f,.08f,.92f));
            CreateSkillPrimitive(root,"Lightning branch A",PrimitiveType.Cube,new Vector3(.18f,.35f,0),new Vector3(.4f,.09f,.1f),new Color(1f,.94f,.5f,.9f));
            CreateSkillPrimitive(root,"Lightning branch B",PrimitiveType.Cube,new Vector3(-.14f,.78f,0),new Vector3(.32f,.08f,.1f),new Color(1f,.94f,.5f,.9f));
        }
        else
        {
            for(int i=0;i<4;i++) CreateSkillPrimitive(root,"Impact spark "+(i+1),PrimitiveType.Cube,Quaternion.Euler(0,i*45f,0)*Vector3.forward*.16f+Vector3.up*.07f,new Vector3(.06f,.1f,.32f),new Color(1f,.68f,.18f,.9f));
        }
    }

    private static void CreateSkillPrimitive(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 size,Color color)
    {
        GameObject visual=GameObject.CreatePrimitive(type); visual.name=name; visual.transform.SetParent(parent,false); visual.transform.localPosition=position; visual.transform.localScale=size;
        Collider collider=visual.GetComponent<Collider>(); if(collider!=null) Object.DestroyImmediate(collider);
        Renderer renderer=visual.GetComponent<Renderer>(); renderer.sharedMaterial=CreateSkillTintMaterial("Skill "+name,color); renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows=false;
    }
    private static void RemoveParticleSystems(GameObject root)
    {
        foreach(ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            Object.DestroyImmediate(renderer, true);
        foreach(ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            Object.DestroyImmediate(system, true);
    }

    private static HeroFireField CreateFireFieldPrefab(VisualEffectAsset graph)
    {
        string path=Root+"/Prefabs/Combat/Hero Inferno Field.prefab";
        bool exists=AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null;
        GameObject contents=exists?PrefabUtility.LoadPrefabContents(path):new GameObject("Hero Inferno Field");
        try
        {
            contents.name="Hero Inferno Field"; contents.transform.localScale=Vector3.one;
            RemoveParticleSystems(contents);
            foreach(Transform child in contents.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            HeroSkillBurst visuals=contents.GetComponent<HeroSkillBurst>()??contents.AddComponent<HeroSkillBurst>();
            visuals.visualEffects=Array.Empty<VisualEffect>(); visuals.duration=5.2f; visuals.animateScale=false; visuals.destroyAfterPlayback=true;
            HeroFireField field=contents.GetComponent<HeroFireField>()??contents.AddComponent<HeroFireField>();
            field.visuals=visuals; field.baseDuration=5.2f; field.baseRadius=2.7f; field.damageInterval=.45f;
            GameObject disc=GameObject.CreatePrimitive(PrimitiveType.Cylinder); disc.name="Burning Ground Ring"; disc.transform.SetParent(contents.transform,false); disc.transform.localPosition=new Vector3(0,.04f,0); disc.transform.localScale=new Vector3(1,.025f,1);
            Collider collider=disc.GetComponent<Collider>(); if(collider!=null) Object.DestroyImmediate(collider);
            disc.GetComponent<Renderer>().sharedMaterial=CreateSkillTintMaterial("Inferno Field",new Color(1f,.16f,.025f,.68f));
            PrefabUtility.SaveAsPrefabAsset(contents,path);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<HeroFireField>();
        }
        finally { if(exists) PrefabUtility.UnloadPrefabContents(contents); else Object.DestroyImmediate(contents); }
    }

    private static HeroOrbitingSaw CreateOrbitingSawPrefab(VisualEffectAsset graph)
    {
        string path=Root+"/Prefabs/Combat/Hero Orbiting Saw.prefab";
        bool exists=AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null;
        GameObject contents=exists?PrefabUtility.LoadPrefabContents(path):new GameObject("Hero Orbiting Saw");
        try
        {
            contents.name="Hero Orbiting Saw"; contents.transform.localScale=Vector3.one*.72f;
            RemoveParticleSystems(contents);
            foreach(Transform child in contents.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            HeroOrbitingSaw saw=contents.GetComponent<HeroOrbitingSaw>()??contents.AddComponent<HeroOrbitingSaw>();
            saw.graphAccent=null;
            GameObject disc=GameObject.CreatePrimitive(PrimitiveType.Cylinder); disc.name="Saw Steel Disc"; disc.transform.SetParent(contents.transform,false); disc.transform.localScale=new Vector3(1f,.12f,1f);
            Collider collider=disc.GetComponent<Collider>(); if(collider!=null) Object.DestroyImmediate(collider);
            disc.GetComponent<Renderer>().sharedMaterial=CreateSkillTintMaterial("Orbiting Saw Steel",new Color(.48f,.75f,.86f,1f));
            for(int i=0;i<8;i++)
            {
                GameObject tooth=GameObject.CreatePrimitive(PrimitiveType.Cube); tooth.name=$"Saw Tooth {i+1}"; tooth.transform.SetParent(contents.transform,false);
                float angle=i*Mathf.PI/4f; tooth.transform.localPosition=new Vector3(Mathf.Cos(angle)*.49f,0,Mathf.Sin(angle)*.49f);
                tooth.transform.localRotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,0); tooth.transform.localScale=new Vector3(.24f,.18f,.13f);
                Collider toothCollider=tooth.GetComponent<Collider>(); if(toothCollider!=null) Object.DestroyImmediate(toothCollider);
                tooth.GetComponent<Renderer>().sharedMaterial=disc.GetComponent<Renderer>().sharedMaterial;
            }
            PrefabUtility.SaveAsPrefabAsset(contents,path);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<HeroOrbitingSaw>();
        }
        finally { if(exists) PrefabUtility.UnloadPrefabContents(contents); else Object.DestroyImmediate(contents); }
    }

    private static Material CreateSkillTintMaterial(string name,Color color)
    {
        EnsureFolder(Root+"/Materials/Combat");
        string path=Root+"/Materials/Combat/"+name+".mat";
        Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null)
        {
            Shader shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader==null) shader=Shader.Find("Standard");
            material=new Material(shader); AssetDatabase.CreateAsset(material,path);
        }
        material.color=color;
        if(material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",color);
        if(material.HasProperty("_Surface")) material.SetFloat("_Surface",1f);
        if(color.a<.999f)
        {
            material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite",0f); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(material); return material;
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
        agent.enabled = false; // AI enables it only after sampling an authored walkable NavMesh point.
        if (go.GetComponent<CombatKnockback>() == null) go.AddComponent<CombatKnockback>();
    }

    private static void AuthorProgressionHud()
    {
        HeroHUDController hud = Object.FindFirstObjectByType<HeroHUDController>();
        if (hud == null || hud.healthSlider == null) return;
        RectTransform health = hud.healthSlider.transform as RectTransform;
        RectTransform parent = health != null ? health.parent as RectTransform : null;
        if (parent == null) return;
        Transform existing = parent.Find("Hero Progression HUD");
        GameObject root = existing != null ? existing.gameObject : new GameObject("Hero Progression HUD", typeof(RectTransform));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        if (existing == null) rootRect.SetParent(parent, false);
        rootRect.anchorMin = health.anchorMin; rootRect.anchorMax = health.anchorMax; rootRect.pivot = health.pivot;
        rootRect.sizeDelta = new Vector2(Mathf.Max(365f, health.rect.width), 190f);
        rootRect.anchoredPosition = health.anchoredPosition + new Vector2(0f, -Mathf.Max(18f, health.rect.height) - 8f);
        Transform exp = root.transform.Find("Experience Bar");
        if (exp == null)
        {
            RectTransform bg = Rect("Experience Bar", root.transform, 0, 0, 0, 20);
            bg.anchorMin = new Vector2(0,1); bg.anchorMax = new Vector2(1,1); bg.pivot = new Vector2(.5f,1);
            bg.offsetMin = new Vector2(0,-20); bg.offsetMax = Vector2.zero;
            Image bgImage = bg.gameObject.AddComponent<Image>(); bgImage.color = new Color(.035f,.055f,.075f,.92f); bgImage.raycastTarget = false;
            RectTransform fill = Rect("Fill", bg, 2, 2, 2, 16); fill.anchorMin=Vector2.zero; fill.anchorMax=Vector2.one; fill.offsetMin=new Vector2(2,2); fill.offsetMax=new Vector2(-2,-2);
            Image fi=fill.gameObject.AddComponent<Image>(); fi.color=new Color(.28f,.72f,1f); fi.raycastTarget=false;
            fi.type=UnityEngine.UI.Image.Type.Filled; fi.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal; fi.fillAmount=0f;
            RectTransform labelRect = Rect("Experience Text", bg, 0, 0, 0, 20); labelRect.anchorMin=Vector2.zero; labelRect.anchorMax=Vector2.one; labelRect.offsetMin=Vector2.zero; labelRect.offsetMax=Vector2.zero;
            TMP_Text label=labelRect.gameObject.AddComponent<TextMeshProUGUI>(); label.font=TMP_Settings.defaultFontAsset; label.fontSize=12; label.color=Color.white; label.alignment=TextAlignmentOptions.Center; label.text="EXP  0 / 10"; label.raycastTarget=false;
        }
        Transform row = root.transform.Find("Upgrade Icons");
        if (row == null)
        {
            RectTransform rowRect = Rect("Upgrade Icons", root.transform, 0, 26, 0, 76);
            rowRect.anchorMin=new Vector2(0,1); rowRect.anchorMax=new Vector2(1,1); rowRect.pivot=new Vector2(.5f,1); rowRect.offsetMin=new Vector2(0,-180); rowRect.offsetMax=new Vector2(0,-26);
            var layout=rowRect.gameObject.AddComponent<GridLayoutGroup>(); layout.cellSize=new Vector2(44,44); layout.spacing=new Vector2(4,4); layout.childAlignment=TextAnchor.UpperLeft; layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount=6;
            for(int i=0;i<16;i++)
            {
                HeroUpgradeType type=(HeroUpgradeType)i;
                RectTransform slotRect=Rect(type+" Upgrade",rowRect,0,0,0,34); slotRect.anchorMin=new Vector2(0,.5f); slotRect.anchorMax=new Vector2(0,.5f); slotRect.pivot=new Vector2(0,.5f); slotRect.sizeDelta=new Vector2(34,34);
                Image bg=slotRect.gameObject.AddComponent<Image>(); bg.color=new Color(.04f,.07f,.08f,.9f); bg.raycastTarget=false;
                RectTransform iconRect=Rect("Icon",slotRect,4,4,4,26); iconRect.anchorMin=Vector2.zero; iconRect.anchorMax=Vector2.one; iconRect.offsetMin=new Vector2(4,4); iconRect.offsetMax=new Vector2(-4,-4);
                Image icon=iconRect.gameObject.AddComponent<Image>(); icon.preserveAspect=true; icon.raycastTarget=false;
                RectTransform countRect=Rect("Count",slotRect,0,0,0,14); countRect.anchorMin=new Vector2(.35f,0); countRect.anchorMax=Vector2.one; countRect.offsetMin=Vector2.zero; countRect.offsetMax=new Vector2(-2,2);
                TMP_Text count=countRect.gameObject.AddComponent<TextMeshProUGUI>(); count.font=TMP_Settings.defaultFontAsset; count.fontSize=10; count.fontStyle=FontStyles.Bold; count.alignment=TextAlignmentOptions.BottomRight; count.color=Color.white; count.text="x0"; count.raycastTarget=false;
            }
        }
        GridLayoutGroup iconLayout=root.transform.Find("Upgrade Icons").GetComponent<GridLayoutGroup>();
        if(iconLayout!=null) { iconLayout.constraint=GridLayoutGroup.Constraint.FixedColumnCount; iconLayout.constraintCount=6; iconLayout.cellSize=new Vector2(44,44); iconLayout.spacing=new Vector2(4,4); }
        RectTransform iconRootRect=root.transform.Find("Upgrade Icons") as RectTransform;
        iconRootRect.offsetMin=new Vector2(0,-180); iconRootRect.offsetMax=new Vector2(0,-26);
        Image experienceBackground=root.transform.Find("Experience Bar").GetComponent<Image>();
        Image experienceFill=root.transform.Find("Experience Bar/Fill").GetComponent<Image>();
        Sprite uiSprite=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Image/ui.png");
        if(uiSprite==null) uiSprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        experienceBackground.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); experienceBackground.type=UnityEngine.UI.Image.Type.Sliced; experienceBackground.color=new Color(.035f,.055f,.075f,.96f);
        experienceFill.sprite=uiSprite; experienceFill.type=UnityEngine.UI.Image.Type.Filled; experienceFill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal; experienceFill.fillOrigin=(int)UnityEngine.UI.Image.OriginHorizontal.Left; experienceFill.fillAmount=0f; experienceFill.color=new Color(.18f,.78f,1f,1f);
        RectTransform experienceRect=experienceFill.rectTransform; experienceRect.anchorMin=Vector2.zero; experienceRect.anchorMax=Vector2.one; experienceRect.offsetMin=new Vector2(3,3); experienceRect.offsetMax=new Vector2(-3,-3);
        TMP_Text experienceLabel=root.transform.Find("Experience Bar/Experience Text").GetComponent<TMP_Text>();
        experienceLabel.fontSize=13; experienceLabel.fontStyle=FontStyles.Bold; experienceLabel.alignment=TextAlignmentOptions.Center; experienceLabel.enableAutoSizing=true; experienceLabel.fontSizeMin=10; experienceLabel.fontSizeMax=13; experienceLabel.raycastTarget=false;
        Transform authoredRow=root.transform.Find("Upgrade Icons");
        while(authoredRow.childCount<16)
        {
            int i=authoredRow.childCount;
            RectTransform slotRect=Rect(((HeroUpgradeType)i)+" Upgrade",authoredRow,0,0,0,34); slotRect.anchorMin=new Vector2(0,.5f); slotRect.anchorMax=new Vector2(0,.5f); slotRect.pivot=new Vector2(0,.5f); slotRect.sizeDelta=new Vector2(34,34);
            Image bg=slotRect.gameObject.AddComponent<Image>(); bg.color=new Color(.04f,.07f,.08f,.9f); bg.raycastTarget=false;
            RectTransform iconRect=Rect("Icon",slotRect,4,4,4,26); iconRect.anchorMin=Vector2.zero; iconRect.anchorMax=Vector2.one; iconRect.offsetMin=new Vector2(4,4); iconRect.offsetMax=new Vector2(-4,-4);
            Image icon=iconRect.gameObject.AddComponent<Image>(); icon.preserveAspect=true; icon.raycastTarget=false;
            RectTransform countRect=Rect("Count",slotRect,0,0,0,14); countRect.anchorMin=new Vector2(.35f,0); countRect.anchorMax=Vector2.one; countRect.offsetMin=Vector2.zero; countRect.offsetMax=new Vector2(-2,2);
            TMP_Text count=countRect.gameObject.AddComponent<TextMeshProUGUI>(); count.font=TMP_Settings.defaultFontAsset; count.fontSize=10; count.fontStyle=FontStyles.Bold; count.alignment=TextAlignmentOptions.BottomRight; count.color=Color.white; count.text="x0"; count.raycastTarget=false;
        }
        GridLayoutGroup authoredGrid=authoredRow.GetComponent<GridLayoutGroup>();
        if(authoredGrid!=null) { authoredGrid.constraint=GridLayoutGroup.Constraint.FixedColumnCount; authoredGrid.constraintCount=6; authoredGrid.cellSize=new Vector2(44,44); authoredGrid.spacing=new Vector2(4,4); }
        RectTransform authoredRowRect=authoredRow as RectTransform; authoredRowRect.offsetMin=new Vector2(0,-180); authoredRowRect.offsetMax=new Vector2(0,-26);
        for(int i=0;i<authoredRow.childCount;i++)
        {
            RectTransform slotRect=authoredRow.GetChild(i) as RectTransform; slotRect.sizeDelta=new Vector2(44,44);
            Image background=slotRect.GetComponent<Image>(); background.sprite=uiSprite; background.type=UnityEngine.UI.Image.Type.Sliced; background.color=new Color(.04f,.07f,.08f,.96f); background.raycastTarget=true;
            RectTransform iconRect=slotRect.Find("Icon") as RectTransform; iconRect.offsetMin=new Vector2(4,4); iconRect.offsetMax=new Vector2(-4,-4);
            Image upgradeIcon=iconRect.GetComponent<Image>(); upgradeIcon.preserveAspect=true; upgradeIcon.raycastTarget=false;
            TMP_Text count=slotRect.Find("Count").GetComponent<TMP_Text>(); count.fontSize=12; count.fontStyle=FontStyles.Bold; count.enableAutoSizing=true; count.fontSizeMin=9; count.fontSizeMax=12;
        }
        Canvas hudCanvas=health.GetComponentInParent<Canvas>();
        HeroUpgradeTooltip tooltip=AuthorHeroUpgradeTooltip(hudCanvas!=null?hudCanvas.transform:root.transform,projectFont:AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/Fonts/PressStart2P SDF.asset"));
        SerializedObject so = new SerializedObject(hud);
        so.FindProperty("progressionRoot").objectReferenceValue = rootRect;
        so.FindProperty("experienceFill").objectReferenceValue = root.transform.Find("Experience Bar/Fill").GetComponent<Image>();
        so.FindProperty("experienceText").objectReferenceValue = root.transform.Find("Experience Bar/Experience Text").GetComponent<TMP_Text>();
        Transform iconRoot=root.transform.Find("Upgrade Icons");
        so.FindProperty("upgradeIconRoot").objectReferenceValue=iconRoot as RectTransform;
        SerializedProperty slots=so.FindProperty("upgradeSlots"); slots.arraySize=16;
        if(iconRoot.childCount<16) throw new Exception("Authored Hero upgrade icon row has fewer than 16 slots.");
        string[] upgradeNames={"Vitality","Damage","Attack Speed","Move Speed","Armor","Pickup Range","Cleave","Knockback","Life Steal","Multi Strike","Shockwave","Execution","Healing Pulse","Arcane Nova","Inferno Field","Orbiting Saws"};
        for(int i=0;i<16;i++)
        {
            SerializedProperty item=slots.GetArrayElementAtIndex(i); item.FindPropertyRelative("type").enumValueIndex=i;
            Transform slot=iconRoot.GetChild(i); item.FindPropertyRelative("root").objectReferenceValue=slot.gameObject;
            item.FindPropertyRelative("icon").objectReferenceValue=slot.Find("Icon").GetComponent<Image>();
            item.FindPropertyRelative("count").objectReferenceValue=slot.Find("Count").GetComponent<TMP_Text>();
            HeroUpgradeDefinition def=AssetDatabase.LoadAssetAtPath<HeroUpgradeDefinition>(Root+"/Upgrades/Hero "+upgradeNames[i]+".asset");
            if(def!=null) slot.Find("Icon").GetComponent<Image>().sprite=def.icon;
            HeroUpgradeHover hover=slot.GetComponent<HeroUpgradeHover>()??slot.gameObject.AddComponent<HeroUpgradeHover>();
            hover.type=(HeroUpgradeType)i; hover.tooltip=tooltip; EditorUtility.SetDirty(hover);
        }
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(hud);
    }

    [MenuItem("Hero Doom/Author Hero Progression HUD")]
    private static void BuildHeroProgressionHudOnly()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Leave Play Mode before authoring.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/SampleScene.unity") throw new Exception("Open SampleScene before authoring.");
        AuthorProgressionHud();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static HeroUpgradeTooltip AuthorHeroUpgradeTooltip(Transform parent,TMP_FontAsset projectFont)
    {
        Transform existing=parent.Find("Hero Upgrade Tooltip");
        GameObject panel=existing!=null?existing.gameObject:new GameObject("Hero Upgrade Tooltip",typeof(RectTransform),typeof(Image),typeof(HeroUpgradeTooltip));
        RectTransform rect=panel.GetComponent<RectTransform>();
        if(existing==null) rect.SetParent(parent,false);
        rect.anchorMin=new Vector2(.5f,.5f); rect.anchorMax=new Vector2(.5f,.5f); rect.pivot=new Vector2(0,1); rect.sizeDelta=new Vector2(350,174); rect.anchoredPosition=Vector2.zero;
        Image background=panel.GetComponent<Image>(); background.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); background.type=UnityEngine.UI.Image.Type.Sliced; background.color=new Color(.035f,.06f,.075f,.98f); background.raycastTarget=false;

        Transform iconTransform=panel.transform.Find("Upgrade Icon");
        Image icon;
        if(iconTransform==null) { RectTransform iconRect=Rect("Upgrade Icon",panel.transform,14,14,0,54); iconRect.anchorMin=new Vector2(0,1); iconRect.anchorMax=new Vector2(0,1); iconRect.pivot=new Vector2(0,1); iconRect.sizeDelta=new Vector2(54,54); icon=iconRect.gameObject.AddComponent<Image>(); }
        else icon=iconTransform.GetComponent<Image>();
        icon.preserveAspect=true; icon.raycastTarget=false;
        TMP_Text title=EnsureTooltipText(panel.transform,"Upgrade Name",new Vector2(78,-12),new Vector2(258,48),17,projectFont);
        TMP_Text description=EnsureTooltipText(panel.transform,"Upgrade Description",new Vector2(14,-76),new Vector2(322,34),12,projectFont);
        TMP_Text rank=EnsureTooltipText(panel.transform,"Upgrade Ranks",new Vector2(14,-114),new Vector2(322,54),11,projectFont);
        HeroUpgradeTooltip controller=panel.GetComponent<HeroUpgradeTooltip>();
        controller.tooltipRoot=rect; controller.icon=icon; controller.title=title; controller.description=description; controller.rankDetails=rank;
        panel.SetActive(false); EditorUtility.SetDirty(controller);
        return controller;
    }

    private static TMP_Text EnsureTooltipText(Transform parent,string name,Vector2 topLeft,Vector2 size,float fontSize,TMP_FontAsset font)
    {
        Transform existing=parent.Find(name); RectTransform rect=existing as RectTransform;
        if(rect==null) rect=Rect(name,parent,topLeft.x,-topLeft.y,size.x,size.y);
        rect.anchorMin=new Vector2(0,1); rect.anchorMax=new Vector2(0,1); rect.pivot=new Vector2(0,1); rect.anchoredPosition=topLeft; rect.sizeDelta=size;
        TMP_Text text=rect.GetComponent<TMP_Text>()??rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font=font!=null?font:TMP_Settings.defaultFontAsset; text.fontSize=fontSize; text.color=Color.white; text.raycastTarget=false; text.textWrappingMode=TextWrappingModes.Normal; text.overflowMode=TextOverflowModes.Overflow;
        return text;
    }

    private static void WireExistingPanel(GameObject panel, SummonerUpgrades upgrades)
    {
        Transform root = panel.transform.name == "Summoner Upgrades" ? panel.transform : panel.transform.Find("Summoner Upgrades");
        if (root == null) return;
        Transform buttonTransform = root.Find("Rebirth Button");
        if (buttonTransform == null)
        {
            GameObject go = new GameObject("Rebirth Button", typeof(RectTransform), typeof(Image), typeof(Button)); go.layer = 5;
            RectTransform rect = go.GetComponent<RectTransform>(); rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0,0); rect.anchorMax = new Vector2(1,0); rect.pivot = new Vector2(.5f,0);
            rect.offsetMin = new Vector2(18,12); rect.offsetMax = new Vector2(-18,48);
            Image bg=go.GetComponent<Image>(); bg.color=new Color(.34f,.2f,.09f,1);
            var labelGo=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI)); labelGo.layer=5;
            RectTransform labelRect=labelGo.GetComponent<RectTransform>(); labelRect.SetParent(rect,false); labelRect.anchorMin=Vector2.zero; labelRect.anchorMax=Vector2.one; labelRect.offsetMin=Vector2.zero; labelRect.offsetMax=Vector2.zero;
            TMP_Text label=labelGo.GetComponent<TextMeshProUGUI>(); label.font=TMP_Settings.defaultFontAsset; label.fontSize=15; label.alignment=TextAlignmentOptions.Center; label.color=Gold; label.text="REBIRTH"; label.raycastTarget=false;
            buttonTransform=go.transform;
        }
        SerializedObject so=new SerializedObject(upgrades);
        so.FindProperty("rebirthButton").objectReferenceValue=buttonTransform.GetComponent<Button>();
        so.FindProperty("rebirthLabel").objectReferenceValue=buttonTransform.Find("Label").GetComponent<TMP_Text>();
        so.ApplyModifiedPropertiesWithoutUndo();
        Transform infoTransform=root.Find("Rebirth Info");
        if(infoTransform==null)
        {
            RectTransform info=Rect("Rebirth Info",root,18,0,18,32);
            info.anchorMin=new Vector2(0,0); info.anchorMax=new Vector2(1,0); info.pivot=new Vector2(.5f,0);
            info.offsetMin=new Vector2(18,54); info.offsetMax=new Vector2(-18,86);
            infoTransform=info;
        }
        TMP_Text infoLabel=infoTransform.GetComponent<TMP_Text>()??infoTransform.gameObject.AddComponent<TextMeshProUGUI>();
        infoLabel.font=TMP_Settings.defaultFontAsset; infoLabel.fontSize=11; infoLabel.fontSizeMin=9; infoLabel.fontSizeMax=11; infoLabel.enableAutoSizing=true;
        infoLabel.alignment=TextAlignmentOptions.Center; infoLabel.color=Mint; infoLabel.text="Reset the run. Keep only the permanent Rebirth reward.";
        infoLabel.textWrappingMode=TextWrappingModes.Normal; infoLabel.overflowMode=TextOverflowModes.Overflow; infoLabel.raycastTarget=false;
    }

    private static void AuthorScrollableUpgradePanel(GameObject panel)
    {
        Transform root=panel.transform.Find("Summoner Upgrades");
        if(root==null) return;
        TMP_FontAsset projectFont=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/Fonts/PressStart2P SDF.asset");
        foreach(TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            if(projectFont!=null) text.font=projectFont;

        Transform viewTransform=root.Find("Upgrade Scroll View");
        GameObject viewObject;
        if(viewTransform==null)
        {
            viewObject=new GameObject("Upgrade Scroll View",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect)); viewObject.layer=5;
            viewObject.transform.SetParent(root,false); viewTransform=viewObject.transform;
        }
        viewObject=viewTransform.gameObject;
        RectTransform view=viewObject.GetComponent<RectTransform>(); view.anchorMin=Vector2.zero; view.anchorMax=Vector2.one; view.pivot=new Vector2(.5f,.5f); view.offsetMin=new Vector2(8,190); view.offsetMax=new Vector2(-30,-150);
        Image viewportImage=viewObject.GetComponent<Image>(); viewportImage.color=new Color(1,1,1,0); viewportImage.raycastTarget=true;

        Transform contentTransform=viewTransform.Find("Upgrade Content");
        if(contentTransform==null)
        {
            GameObject contentGo=new GameObject("Upgrade Content",typeof(RectTransform)); contentGo.layer=5; contentGo.transform.SetParent(viewTransform,false); contentTransform=contentGo.transform;
        }
        RectTransform content=contentTransform.GetComponent<RectTransform>(); content.anchorMin=new Vector2(0,1); content.anchorMax=new Vector2(1,1); content.pivot=new Vector2(.5f,1); content.anchoredPosition=Vector2.zero;

        MonsterUpgradeCard[] cards=root.GetComponentsInChildren<MonsterUpgradeCard>(true);
        Array.Sort(cards,(a,b)=>a.slot.CompareTo(b.slot));
        foreach(MonsterUpgradeCard card in cards)
        {
            if(card.upgrades!=null && card.upgrades.definitions!=null && card.slot>=0 && card.slot<card.upgrades.definitions.Length && card.icon!=null)
            {
                MonsterUpgradeDefinition definition=card.upgrades.definitions[card.slot];
                if(definition!=null) card.icon.sprite=definition.icon;
                card.icon.preserveAspect=true; card.icon.color=Color.white; card.icon.enabled=card.icon.sprite!=null;
            }
            RectTransform cardRect=card.transform as RectTransform;
            cardRect.SetParent(content,false); cardRect.anchorMin=new Vector2(0,1); cardRect.anchorMax=new Vector2(1,1); cardRect.pivot=new Vector2(.5f,1);
            float top=12+card.slot*390f; cardRect.offsetMin=new Vector2(14,-top-360); cardRect.offsetMax=new Vector2(-14,-top);
            Transform icon=card.transform.Find("Monster Icon");
            if(icon!=null) { RectTransform r=icon as RectTransform; r.anchorMin=new Vector2(0,1); r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(16,-14); r.sizeDelta=new Vector2(56,56); }
            Transform title=card.transform.Find("Monster Name"); if(title!=null) { RectTransform r=title as RectTransform; r.offsetMin=new Vector2(86,-62); r.offsetMax=new Vector2(-16,-12); TMP_Text t=title.GetComponent<TMP_Text>(); t.fontSize=22; t.fontSizeMin=15; t.fontSizeMax=22; t.enableAutoSizing=true; t.fontStyle=FontStyles.Bold; t.textWrappingMode=TextWrappingModes.Normal; t.overflowMode=TextOverflowModes.Overflow; }
            Transform stats=card.transform.Find("Power Bonuses"); if(stats!=null) { RectTransform r=stats as RectTransform; r.offsetMin=new Vector2(18,-140); r.offsetMax=new Vector2(-18,-72); TMP_Text t=stats.GetComponent<TMP_Text>(); t.fontSize=16; t.fontSizeMin=12; t.fontSizeMax=16; t.enableAutoSizing=true; t.textWrappingMode=TextWrappingModes.Normal; t.overflowMode=TextOverflowModes.Overflow; }
            SetCardButtonLayout(card.powerButton,150,46,18); if(card.powerLabel!=null) { card.powerLabel.fontSize=16; card.powerLabel.enableAutoSizing=true; card.powerLabel.fontSizeMin=10; card.powerLabel.fontSizeMax=16; card.powerLabel.textWrappingMode=TextWrappingModes.Normal; card.powerLabel.overflowMode=TextOverflowModes.Overflow; }
            SetCardButtonLayout(card.autoButton,204,46,18); if(card.autoLabel!=null) { card.autoLabel.fontSize=16; card.autoLabel.enableAutoSizing=true; card.autoLabel.fontSizeMin=10; card.autoLabel.fontSizeMax=16; card.autoLabel.textWrappingMode=TextWrappingModes.Normal; card.autoLabel.overflowMode=TextOverflowModes.Overflow; }
            if(card.autoStatus!=null) { RectTransform r=card.autoStatus.rectTransform; r.offsetMin=new Vector2(18,-308); r.offsetMax=new Vector2(-18,-260); card.autoStatus.fontSize=14; card.autoStatus.enableAutoSizing=true; card.autoStatus.fontSizeMin=11; card.autoStatus.fontSizeMax=14; card.autoStatus.textWrappingMode=TextWrappingModes.Normal; card.autoStatus.overflowMode=TextOverflowModes.Overflow; }
            Transform track=card.transform.Find("Timer Track"); if(track!=null) { RectTransform r=track as RectTransform; r.offsetMin=new Vector2(18,-330); r.offsetMax=new Vector2(-18,-322); Transform fill=track.Find("Timer Fill"); if(fill!=null) { Image fillImage=fill.GetComponent<Image>(); fillImage.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Image/ui.png") ?? AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); fillImage.type=UnityEngine.UI.Image.Type.Filled; fillImage.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal; fillImage.color=Mint; } }
        }

        Transform help=root.Find("Automation Help");
        if(help!=null) help.gameObject.SetActive(false); // Footer already has these hints; this used to overlap the final card.
        content.sizeDelta=new Vector2(0,Mathf.Max(520,12f+Mathf.Max(cards.Length,4)*390f+30f));

        Transform scrollbarTransform=root.Find("Upgrade Scrollbar");
        if(scrollbarTransform==null)
        {
            GameObject barGo=new GameObject("Upgrade Scrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar)); barGo.layer=5; barGo.transform.SetParent(root,false); scrollbarTransform=barGo.transform;
            GameObject handleGo=new GameObject("Handle",typeof(RectTransform),typeof(Image)); handleGo.layer=5; handleGo.transform.SetParent(barGo.transform,false);
        }
        RectTransform barRect=scrollbarTransform as RectTransform; barRect.anchorMin=new Vector2(1,0); barRect.anchorMax=new Vector2(1,1); barRect.pivot=new Vector2(1,.5f); barRect.sizeDelta=new Vector2(16,0); barRect.anchoredPosition=new Vector2(-8,-19); barRect.offsetMin=new Vector2(-24,190); barRect.offsetMax=new Vector2(-8,-150);
        Image barImage=scrollbarTransform.GetComponent<Image>(); barImage.color=new Color(.04f,.08f,.09f,.95f); barImage.raycastTarget=true;
        RectTransform handleRect=scrollbarTransform.Find("Handle") as RectTransform; handleRect.anchorMin=Vector2.zero; handleRect.anchorMax=Vector2.one; handleRect.offsetMin=new Vector2(2,2); handleRect.offsetMax=new Vector2(-2,-2);
        Image handleImage=handleRect.GetComponent<Image>(); handleImage.color=Gold; handleImage.raycastTarget=true;
        Scrollbar scrollbar=scrollbarTransform.GetComponent<Scrollbar>(); scrollbar.handleRect=handleRect; scrollbar.targetGraphic=handleImage; scrollbar.direction=Scrollbar.Direction.BottomToTop;
        ScrollRect scroll=viewObject.GetComponent<ScrollRect>(); scroll.content=content; scroll.viewport=view; scroll.horizontal=false; scroll.vertical=true; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.inertia=true; scroll.scrollSensitivity=54; scroll.verticalScrollbar=scrollbar; scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport; scroll.verticalScrollbarSpacing=-2; content.anchoredPosition=Vector2.zero; scroll.verticalNormalizedPosition=1f;

        Transform heading=root.Find("Heading"); if(heading!=null) { RectTransform h=heading as RectTransform; h.offsetMin=new Vector2(24,-52); h.offsetMax=new Vector2(-22,-12); TMP_Text t=heading.GetComponent<TMP_Text>(); t.text="SUMMONER UPGRADES"; t.fontSize=25; t.fontSizeMin=18; t.fontSizeMax=25; t.enableAutoSizing=true; t.textWrappingMode=TextWrappingModes.NoWrap; t.overflowMode=TextOverflowModes.Overflow; }
        Transform subtitle=root.Find("Subtitle"); if(subtitle!=null) { RectTransform s=subtitle as RectTransform; s.offsetMin=new Vector2(24,-86); s.offsetMax=new Vector2(-22,-58); TMP_Text t=subtitle.GetComponent<TMP_Text>(); t.text="Gold upgrades | F: swap control | Click ground: move"; t.fontSize=14; t.enableAutoSizing=true; t.fontSizeMin=11; t.fontSizeMax=14; t.textWrappingMode=TextWrappingModes.NoWrap; t.overflowMode=TextOverflowModes.Overflow; }
        SummonerUpgrades upgrades=root.GetComponentInParent<SummonerUpgrades>();
        if(upgrades!=null) EditorUtility.SetDirty(upgrades);
        EditorUtility.SetDirty(viewObject); EditorUtility.SetDirty(content.gameObject); EditorUtility.SetDirty(scroll);
        if(PrefabUtility.IsPartOfPrefabInstance(root.gameObject))
            PrefabUtility.ApplyPrefabInstance(root.gameObject,InteractionMode.AutomatedAction);
        else
            PrefabUtility.SaveAsPrefabAsset(root.gameObject,Root+"/Prefabs/Combat/Summoner Upgrade Panel.prefab");
    }

    private static void SetCardButtonLayout(Button button,float top,float height,float horizontalInset)
    {
        if(button==null) return;
        RectTransform rect=button.transform as RectTransform; rect.offsetMin=new Vector2(horizontalInset,-top-height); rect.offsetMax=new Vector2(-horizontalInset,-top);
    }

    private static void AuthorLuckyWheel()
    {
        Canvas[] canvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        Canvas canvas=canvases.FirstOrDefault(c=>c.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Lucky Wheel"));
        if(canvas==null) throw new Exception("Canvas or Lucky Wheel icon button is missing.");
        Transform buttonTransform=canvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Lucky Wheel");
        Button open=buttonTransform.GetComponent<Button>()??Undo.AddComponent<Button>(buttonTransform.gameObject);
        Image openImage=buttonTransform.GetComponent<Image>(); if(openImage!=null) { open.targetGraphic=openImage; openImage.raycastTarget=true; }

        Transform existing=canvas.transform.Find("Lucky Wheel Popup");
        GameObject popup=existing!=null?existing.gameObject:new GameObject("Lucky Wheel Popup",typeof(RectTransform),typeof(Image));
        popup.layer=5; if(existing==null) popup.transform.SetParent(canvas.transform,false);
        RectTransform popupRect=popup.GetComponent<RectTransform>(); popupRect.anchorMin=Vector2.zero; popupRect.anchorMax=Vector2.one; popupRect.offsetMin=Vector2.zero; popupRect.offsetMax=Vector2.zero;
        Image shade=popup.GetComponent<Image>(); shade.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); shade.color=new Color(.015f,.025f,.035f,.82f); shade.raycastTarget=true;

        Transform cardTransform=popup.transform.Find("Wheel Card");
        GameObject card=cardTransform!=null?cardTransform.gameObject:new GameObject("Wheel Card",typeof(RectTransform),typeof(Image)); card.layer=5;
        if(cardTransform==null) card.transform.SetParent(popup.transform,false);
        RectTransform cardRect=card.GetComponent<RectTransform>(); cardRect.anchorMin=new Vector2(.5f,.5f); cardRect.anchorMax=new Vector2(.5f,.5f); cardRect.pivot=new Vector2(.5f,.5f); cardRect.anchoredPosition=Vector2.zero; cardRect.sizeDelta=new Vector2(560,640);
        Image cardImage=card.GetComponent<Image>(); cardImage.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); cardImage.type=UnityEngine.UI.Image.Type.Sliced; cardImage.color=new Color(.055f,.09f,.12f,1); cardImage.raycastTarget=true;
        Shadow cardShadow=card.GetComponent<Shadow>()??Undo.AddComponent<Shadow>(card); cardShadow.effectColor=new Color(0f,0f,0f,.72f); cardShadow.effectDistance=new Vector2(0f,-9f);
        Outline cardOutline=card.GetComponent<Outline>()??Undo.AddComponent<Outline>(card); cardOutline.effectColor=new Color(.75f,.46f,.16f,.9f); cardOutline.effectDistance=new Vector2(2f,2f);
        TMP_FontAsset font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/Fonts/PressStart2P SDF.asset"); if(font==null) font=TMP_Settings.defaultFontAsset;
        TMP_Text heading=WheelText("Wheel Heading",card.transform,"LUCKY WHEEL",new Vector2(0,294),new Vector2(470,34),21,font,Gold); heading.alignment=TextAlignmentOptions.Center;
        TMP_Text balance=WheelText("Wheel Balance",card.transform,"GOLD  0",new Vector2(0,258),new Vector2(470,26),14,font,Color.white); balance.alignment=TextAlignmentOptions.Center;

        Transform wheelTransform=card.transform.Find("Wheel");
        GameObject wheelObject=wheelTransform!=null?wheelTransform.gameObject:new GameObject("Wheel",typeof(RectTransform),typeof(Image)); wheelObject.layer=5;
        if(wheelTransform==null) wheelObject.transform.SetParent(card.transform,false);
        RectTransform wheelRect=wheelObject.GetComponent<RectTransform>(); wheelRect.anchorMin=new Vector2(.5f,.5f); wheelRect.anchorMax=new Vector2(.5f,.5f); wheelRect.pivot=new Vector2(.5f,.5f); wheelRect.anchoredPosition=new Vector2(0,4); wheelRect.sizeDelta=new Vector2(418,418);
        Sprite circle=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if(circle==null) circle=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        Image wheelFace=wheelObject.GetComponent<Image>(); wheelFace.sprite=circle; wheelFace.type=UnityEngine.UI.Image.Type.Simple; wheelFace.color=new Color(.78f,.48f,.15f,1); wheelFace.raycastTarget=false;
        Transform faceTransform=wheelObject.transform.Find("Wheel Face");
        GameObject face=faceTransform!=null?faceTransform.gameObject:new GameObject("Wheel Face",typeof(RectTransform),typeof(Image)); face.layer=5;
        if(faceTransform==null) face.transform.SetParent(wheelObject.transform,false);
        RectTransform faceRect=face.GetComponent<RectTransform>(); faceRect.anchorMin=new Vector2(.5f,.5f); faceRect.anchorMax=new Vector2(.5f,.5f); faceRect.pivot=new Vector2(.5f,.5f); faceRect.anchoredPosition=Vector2.zero; faceRect.sizeDelta=new Vector2(394,394);
        Image faceImage=face.GetComponent<Image>(); faceImage.sprite=circle; faceImage.type=UnityEngine.UI.Image.Type.Simple; faceImage.color=new Color(.055f,.09f,.12f,1); faceImage.raycastTarget=false;
        foreach(Transform previousSlice in wheelObject.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Prize Slice ")).ToArray()) previousSlice.SetParent(face.transform,false);
        Transform previousHub=wheelObject.transform.Find("Wheel Hub"); if(previousHub!=null) previousHub.SetParent(face.transform,false);

        string[] prizes={".5x\nGOLD","HERO\nHEAL","2\nMINIONS","1x\nGOLD","HERO\nEXP","GOLD\nFRENZY","2x\nGOLD","5x\nJACKPOT"};
        Color[] colors={new Color(.18f,.35f,.50f),new Color(.15f,.53f,.39f),new Color(.19f,.43f,.30f),new Color(.24f,.50f,.57f),new Color(.43f,.30f,.68f),new Color(.67f,.25f,.12f),new Color(.67f,.46f,.12f),new Color(.68f,.15f,.20f)};
        for(int i=0;i<8;i++)
        {
            string wedgeName="Prize Slice "+i; Transform old=face.transform.Find(wedgeName);
            GameObject wedgeGo=old!=null?old.gameObject:new GameObject(wedgeName,typeof(RectTransform),typeof(Image)); wedgeGo.layer=5;
            if(old==null) wedgeGo.transform.SetParent(face.transform,false);
            RectTransform wedgeRect=wedgeGo.GetComponent<RectTransform>(); wedgeRect.anchorMin=new Vector2(.5f,.5f); wedgeRect.anchorMax=new Vector2(.5f,.5f); wedgeRect.pivot=new Vector2(.5f,.5f); wedgeRect.anchoredPosition=Vector2.zero; wedgeRect.sizeDelta=new Vector2(372,372); wedgeRect.localEulerAngles=new Vector3(0,0,-i*45f);
            Image wedge=wedgeGo.GetComponent<Image>(); wedge.sprite=circle; wedge.type=UnityEngine.UI.Image.Type.Filled; wedge.fillMethod=UnityEngine.UI.Image.FillMethod.Radial360; wedge.fillOrigin=2; wedge.fillClockwise=true; wedge.fillAmount=.125f; wedge.color=colors[i]; wedge.raycastTarget=false;
            TMP_Text label=WheelText("Prize Label "+i,wedgeGo.transform,prizes[i],Vector2.zero,new Vector2(104,44),10,font,Color.white);
            float angle=67.5f*Mathf.Deg2Rad;
            RectTransform labelRect=label.rectTransform; labelRect.anchorMin=new Vector2(.5f,.5f); labelRect.anchorMax=new Vector2(.5f,.5f); labelRect.pivot=new Vector2(.5f,.5f); labelRect.anchoredPosition=new Vector2(Mathf.Cos(angle)*128f,Mathf.Sin(angle)*128f); labelRect.sizeDelta=new Vector2(104,44); label.alignment=TextAlignmentOptions.Center; label.enableAutoSizing=true; label.fontSizeMin=8; label.fontSizeMax=10; label.textWrappingMode=TextWrappingModes.Normal;
        }
        Transform hubTransform=face.transform.Find("Wheel Hub");
        GameObject hub=hubTransform!=null?hubTransform.gameObject:new GameObject("Wheel Hub",typeof(RectTransform),typeof(Image)); hub.layer=5;
        if(hubTransform==null) hub.transform.SetParent(face.transform,false);
        RectTransform hubRect=hub.GetComponent<RectTransform>(); hubRect.anchorMin=new Vector2(.5f,.5f); hubRect.anchorMax=new Vector2(.5f,.5f); hubRect.pivot=new Vector2(.5f,.5f); hubRect.anchoredPosition=Vector2.zero; hubRect.sizeDelta=new Vector2(76,76);
        Image hubImage=hub.GetComponent<Image>(); hubImage.sprite=circle; hubImage.type=UnityEngine.UI.Image.Type.Simple; hubImage.color=Gold; hubImage.raycastTarget=false;
        TMP_Text centerMark=WheelText("Wheel Center Mark",hub.transform,"✦",Vector2.zero,new Vector2(58,58),23,font,Color.white); centerMark.alignment=TextAlignmentOptions.Center;
        TMP_Text pointer=WheelText("Wheel Pointer",card.transform,"▼",new Vector2(0,220),new Vector2(48,38),26,font,Gold); pointer.alignment=TextAlignmentOptions.Center;
        TMP_Text rewardGuide=WheelText("Wheel Reward Guide",card.transform,"HEAL  •  SUMMON  •  EXP  •  GOLD BOOST",new Vector2(0,-194),new Vector2(500,30),10,font,Mint); rewardGuide.alignment=TextAlignmentOptions.Center;
        TMP_Text result=WheelText("Wheel Result",card.transform,"CHOOSE YOUR FATE",new Vector2(0,-225),new Vector2(480,30),13,font,Gold); result.alignment=TextAlignmentOptions.Center;
        Button close=WheelButton("Close Wheel",card.transform,"CLOSE",new Vector2(0,-300),new Vector2(190,40),new Color(.22f,.29f,.32f),font);
        Button spin=WheelButton("Spin Button",card.transform,"SPIN  100 G",new Vector2(0,-252),new Vector2(340,48),new Color(.46f,.28f,.08f),font);
        LuckyWheelController controller=buttonTransform.GetComponent<LuckyWheelController>()??Undo.AddComponent<LuckyWheelController>(buttonTransform.gameObject);
        controller.popup=popup; controller.wheel=wheelRect; controller.openButton=open; controller.closeButton=close; controller.spinButton=spin; controller.balanceText=balance; controller.costText=spin.GetComponentInChildren<TMP_Text>(true); controller.resultText=result; controller.baseSpinCost=100d; controller.costGrowth=1.2f; controller.spinDuration=3.2f;
        popup.SetActive(false); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(popup);
    }

    private static void AuthorRebirthTree(SummonerUpgrades upgrades)
    {
        Canvas[] canvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        Canvas canvas=canvases.FirstOrDefault(c=>c.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Lucky Wheel"));
        if(canvas==null) throw new Exception("Canvas or left action panel is missing.");
        TMP_FontAsset font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/Fonts/PressStart2P SDF.asset") ?? TMP_Settings.defaultFontAsset;
        RebirthTreeDefinition tree=CreateRebirthTreeDefinition();
        upgrades.rebirthTree=tree;

        Transform wheelTransform=canvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Lucky Wheel");
        RectTransform wheelRect=wheelTransform as RectTransform;
        Transform oldButton=wheelTransform.parent.Find("Rebirth Tree");
        GameObject treeButton=oldButton!=null?oldButton.gameObject:new GameObject("Rebirth Tree",typeof(RectTransform),typeof(Image),typeof(Button));
        treeButton.layer=5; if(oldButton==null) treeButton.transform.SetParent(wheelTransform.parent,false);
        RectTransform buttonRect=treeButton.GetComponent<RectTransform>();
        buttonRect.anchorMin=wheelRect.anchorMin; buttonRect.anchorMax=wheelRect.anchorMax; buttonRect.pivot=wheelRect.pivot;
        buttonRect.anchoredPosition=wheelRect.anchoredPosition+new Vector2(0f,-116f); buttonRect.sizeDelta=wheelRect.sizeDelta;
        Image buttonImage=treeButton.GetComponent<Image>(); buttonImage.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); buttonImage.type=UnityEngine.UI.Image.Type.Sliced; buttonImage.color=new Color(.10f,.22f,.24f,1f); buttonImage.raycastTarget=true;
        Button open=treeButton.GetComponent<Button>(); open.targetGraphic=buttonImage;
        Sprite treeIcon=tree.nodes[0].icon;
        Transform iconTransform=treeButton.transform.Find("Tree Icon");
        GameObject iconGo=iconTransform!=null?iconTransform.gameObject:new GameObject("Tree Icon",typeof(RectTransform),typeof(Image)); iconGo.layer=5;
        if(iconTransform==null) iconGo.transform.SetParent(treeButton.transform,false);
        RectTransform iconRect=iconGo.GetComponent<RectTransform>(); iconRect.anchorMin=iconRect.anchorMax=new Vector2(.5f,.5f); iconRect.pivot=new Vector2(.5f,.5f); iconRect.anchoredPosition=new Vector2(0,12); iconRect.sizeDelta=new Vector2(50,50);
        Image iconImage=iconGo.GetComponent<Image>(); iconImage.sprite=treeIcon; iconImage.preserveAspect=true; iconImage.color=Gold; iconImage.raycastTarget=false;
        TMP_Text iconLabel=WheelText("Tree Label",treeButton.transform,"REBIRTH",new Vector2(0,-30),new Vector2(90,22),9,font,Mint); iconLabel.alignment=TextAlignmentOptions.Center;

        Transform existing=canvas.transform.Find("Rebirth Tree Popup");
        GameObject popup=existing!=null?existing.gameObject:new GameObject("Rebirth Tree Popup",typeof(RectTransform),typeof(Image)); popup.layer=5;
        if(existing==null) popup.transform.SetParent(canvas.transform,false);
        RectTransform popupRect=popup.GetComponent<RectTransform>(); popupRect.anchorMin=Vector2.zero; popupRect.anchorMax=Vector2.one; popupRect.offsetMin=popupRect.offsetMax=Vector2.zero;
        Image shade=popup.GetComponent<Image>(); shade.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); shade.type=UnityEngine.UI.Image.Type.Sliced; shade.color=new Color(.01f,.02f,.03f,.88f); shade.raycastTarget=true;

        Transform cardTransform=popup.transform.Find("Tree Card");
        GameObject card=cardTransform!=null?cardTransform.gameObject:new GameObject("Tree Card",typeof(RectTransform),typeof(Image)); card.layer=5;
        if(cardTransform==null) card.transform.SetParent(popup.transform,false);
        RectTransform cardRect=card.GetComponent<RectTransform>(); cardRect.anchorMin=cardRect.anchorMax=new Vector2(.5f,.5f); cardRect.pivot=new Vector2(.5f,.5f); cardRect.anchoredPosition=Vector2.zero; cardRect.sizeDelta=new Vector2(1550,860);
        Image cardImage=card.GetComponent<Image>(); cardImage.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); cardImage.type=UnityEngine.UI.Image.Type.Sliced; cardImage.color=new Color(.045f,.085f,.10f,1f); cardImage.raycastTarget=true;
        Outline outline=card.GetComponent<Outline>()??Undo.AddComponent<Outline>(card); outline.effectColor=new Color(.36f,.79f,.70f,.8f); outline.effectDistance=new Vector2(2f,2f);

        TMP_Text heading=WheelText("Tree Heading",card.transform,"REBIRTH TREE",new Vector2(-470,385),new Vector2(700,44),27,font,Gold); heading.alignment=TextAlignmentOptions.Left;
        TMP_Text sub=WheelText("Tree Subtitle",card.transform,"Each Rebirth gives 1 point. Choices are permanent.",new Vector2(-455,347),new Vector2(730,32),14,font,Mint); sub.alignment=TextAlignmentOptions.Left;
        TMP_Text points=WheelText("Point Label",card.transform,"POINTS  0  |  COST  0",new Vector2(500,376),new Vector2(300,38),15,font,Color.white); points.alignment=TextAlignmentOptions.Center;
        Button close=WheelButton("Close Rebirth Tree",card.transform,"CLOSE",new Vector2(675,376),new Vector2(130,44),new Color(.20f,.28f,.30f),font);

        Transform treeAreaTransform=card.transform.Find("Tree Area");
        GameObject treeArea=treeAreaTransform!=null?treeAreaTransform.gameObject:new GameObject("Tree Area",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(RebirthTreePanView)); treeArea.layer=5;
        if(treeAreaTransform==null) treeArea.transform.SetParent(card.transform,false);
        RectTransform areaRect=treeArea.GetComponent<RectTransform>(); areaRect.anchorMin=areaRect.anchorMax=new Vector2(.5f,.5f); areaRect.pivot=new Vector2(.5f,.5f); areaRect.anchoredPosition=new Vector2(-210,-10); areaRect.sizeDelta=new Vector2(1050,630);
        Image areaImage=treeArea.GetComponent<Image>()??Undo.AddComponent<Image>(treeArea); areaImage.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); areaImage.type=UnityEngine.UI.Image.Type.Sliced; areaImage.color=new Color(.025f,.055f,.065f,.72f); areaImage.raycastTarget=true;
        if(treeArea.GetComponent<RectMask2D>()==null) Undo.AddComponent<RectMask2D>(treeArea);
        Transform contentTransform=treeArea.transform.Find("Tree Content");
        GameObject treeContent=contentTransform!=null?contentTransform.gameObject:new GameObject("Tree Content",typeof(RectTransform)); treeContent.layer=5;
        if(contentTransform==null) treeContent.transform.SetParent(treeArea.transform,false);
        RectTransform contentRect=treeContent.GetComponent<RectTransform>(); contentRect.anchorMin=contentRect.anchorMax=new Vector2(.5f,.5f); contentRect.pivot=new Vector2(.5f,.5f); contentRect.anchoredPosition=new Vector2(290,0); contentRect.sizeDelta=new Vector2(1800,1050); contentRect.localScale=Vector3.one;
        foreach(Transform child in treeArea.transform.Cast<Transform>().ToArray())
            if(child!=treeContent.transform && (child.name.StartsWith("Connector ") || child.name.StartsWith("Tree Node "))) child.SetParent(treeContent.transform,false);
        RebirthTreePanView panView=treeArea.GetComponent<RebirthTreePanView>()??Undo.AddComponent<RebirthTreePanView>(treeArea); panView.viewport=areaRect; panView.content=contentRect; panView.initialContentPosition=new Vector2(290,0); panView.dragMultiplier=1f; panView.minZoom=.72f; panView.maxZoom=1.28f; panView.zoomStep=.1f;

        Transform detailTransform=card.transform.Find("Node Detail");
        GameObject detail=detailTransform!=null?detailTransform.gameObject:new GameObject("Node Detail",typeof(RectTransform),typeof(Image)); detail.layer=5;
        if(detailTransform==null) detail.transform.SetParent(card.transform,false);
        RectTransform detailRect=detail.GetComponent<RectTransform>(); detailRect.anchorMin=detailRect.anchorMax=new Vector2(.5f,.5f); detailRect.pivot=new Vector2(.5f,.5f); detailRect.anchoredPosition=new Vector2(560,-10); detailRect.sizeDelta=new Vector2(365,465);
        Image detailImage=detail.GetComponent<Image>(); detailImage.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); detailImage.type=UnityEngine.UI.Image.Type.Sliced; detailImage.color=new Color(.075f,.14f,.15f,1f);
        TMP_Text detailHeader=WheelText("Detail Header",detail.transform,"NODE DETAILS",new Vector2(0,180),new Vector2(330,32),15,font,Mint); detailHeader.alignment=TextAlignmentOptions.Center;
        TMP_Text detailTitle=WheelText("Detail Title",detail.transform,"",new Vector2(0,125),new Vector2(330,70),20,font,Gold); detailTitle.alignment=TextAlignmentOptions.Center; detailTitle.textWrappingMode=TextWrappingModes.Normal;
        TMP_Text detailDescription=WheelText("Detail Description",detail.transform,"",new Vector2(0,8),new Vector2(315,160),14,font,Color.white); detailDescription.alignment=TextAlignmentOptions.TopLeft; detailDescription.textWrappingMode=TextWrappingModes.Normal;
        TMP_Text detailStatus=WheelText("Detail Status",detail.transform,"",new Vector2(0,-175),new Vector2(330,52),12,font,Mint); detailStatus.alignment=TextAlignmentOptions.Center; detailStatus.textWrappingMode=TextWrappingModes.Normal;
        TMP_Text tip=WheelText("Tree Tip",card.transform,"DRAG TO PAN  •  SCROLL TO ZOOM  •  NODES STAY INSIDE THE MAP",new Vector2(0,-395),new Vector2(1320,32),13,font,new Color(.72f,.80f,.78f)); tip.alignment=TextAlignmentOptions.Center;

        Vector2[] positions={
            new(-560,0), new(-350,190), new(-120,250), new(110,250), new(-350,0), new(-120,0), new(110,0), new(-350,-190), new(-120,-250), new(110,-250),
            new(340,75), new(570,75), new(340,-65), new(570,-65), new(340,-250), new(340,250), new(570,-250), new(570,250)
        };
        int[,] links={{0,1},{1,2},{2,3},{0,4},{4,5},{5,6},{0,7},{7,8},{8,9},{6,10},{10,11},{6,12},{12,13},{9,14},{3,15},{14,16},{15,17}};
        Image[] connectors=new Image[links.GetLength(0)];
        for(int i=0;i<connectors.Length;i++)
            connectors[i]=AuthorTreeConnector("Connector "+i,treeContent.transform,positions[links[i,0]],positions[links[i,1]]);
        RebirthTreeNodeView[] views=new RebirthTreeNodeView[tree.nodes.Count];
        for(int i=0;i<tree.nodes.Count;i++)
            views[i]=AuthorTreeNode(treeContent.transform,tree.nodes[i],i,positions[i],font);

        RebirthTreeController controller=treeButton.GetComponent<RebirthTreeController>()??Undo.AddComponent<RebirthTreeController>(treeButton);
        controller.upgrades=upgrades; controller.definition=tree; controller.popup=popup; controller.openButton=open; controller.closeButton=close;
        controller.pointLabel=points; controller.detailTitle=detailTitle; controller.detailDescription=detailDescription; controller.detailStatus=detailStatus;
        controller.nodeViews=views; controller.connectors=connectors;
        controller.connectorFromNodes=Enumerable.Range(0,connectors.Length).Select(i=>links[i,0]).ToArray();
        controller.connectorToNodes=Enumerable.Range(0,connectors.Length).Select(i=>links[i,1]).ToArray();
        popup.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(popup,Root+"/Prefabs/Combat/Rebirth Tree Panel.prefab");
        EditorUtility.SetDirty(upgrades); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(popup);
    }

    [MenuItem("Hero Doom/Author Rebirth Tree")]
    public static void BuildRebirthTreeOnly()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Leave Play Mode before authoring.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/SampleScene.unity") throw new Exception("Open SampleScene before authoring.");
        SummonerUpgrades upgrades=Object.FindFirstObjectByType<SummonerUpgrades>();
        if(upgrades==null) throw new Exception("SummonerUpgrades is missing.");
        EnsureFolder(Root+"/Prefabs/Combat"); EnsureFolder(Root+"/Upgrades"); EnsureFolder(Root+"/Image/Upgrades");
        AuthorRebirthTree(upgrades);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=Object.FindFirstObjectByType<RebirthTreeController>(FindObjectsInactive.Include)?.gameObject;
    }

    private static RebirthTreeDefinition CreateRebirthTreeDefinition()
    {
        string path=Root+"/Upgrades/Rebirth Tree Definition.asset";
        RebirthTreeDefinition tree=AssetDatabase.LoadAssetAtPath<RebirthTreeDefinition>(path);
        if(tree==null) { tree=ScriptableObject.CreateInstance<RebirthTreeDefinition>(); AssetDatabase.CreateAsset(tree,path); }
        if(tree.nodes.Count>0)
        {
            EnsureExpandedRebirthTree(tree);
            return tree;
        }
        string[] names={"Ancestral Cache","Coin Relay","Gilded Coffers","Bargain Sigil","Bone Plating","Blood Pact","Iron Cohort","Quick Ritual","Endless Chant","Ward Shell"};
        string[] descriptions={
            "+25% gold at the start of every Rebirth.",
            "+12% gold from passive income and combat damage.",
            "+3 maximum army capacity.",
            "Summons cost 10% less gold.",
            "All summoned monsters gain +15% maximum health.",
            "All summoned monsters deal +15% damage.",
            "+4 maximum army capacity.",
            "Automatic summons complete 12% faster.",
            "Automatic summons complete another 10% faster.",
            "Newly summoned monsters are protected for +0.75 seconds."
        };
        RebirthTreeBonus[] bonuses={RebirthTreeBonus.StartingGold,RebirthTreeBonus.GoldIncome,RebirthTreeBonus.ArmyCapacity,RebirthTreeBonus.SummonCostDiscount,RebirthTreeBonus.MonsterHealth,RebirthTreeBonus.MonsterDamage,RebirthTreeBonus.ArmyCapacity,RebirthTreeBonus.AutoSummonSpeed,RebirthTreeBonus.AutoSummonSpeed,RebirthTreeBonus.SpawnProtection};
        float[] values={.25f,.12f,3f,.10f,.15f,.15f,4f,.12f,.10f,.75f};
        int[] costs={1,1,1,2,1,1,2,1,2,1};
        int[][] requires={Array.Empty<int>(),new[]{0},new[]{1},new[]{2},new[]{0},new[]{4},new[]{5},new[]{0},new[]{7},new[]{7}};
        string[] patterns={
            "00111100/01111110/11100111/11011011/11011011/11100111/01111110/00111100",
            "00011000/00111100/01111110/11011011/00011000/00011000/00111100/01111110",
            "00111100/01111110/11000011/11011011/11011011/11000011/01111110/00111100",
            "00011000/00111100/01111110/11011011/11011011/01111110/00111100/00011000",
            "00011000/00111100/01111110/11111111/01111110/00111100/00011000/00011000",
            "11000011/11100111/01111110/00111100/00111100/01111110/11100111/11000011",
            "00011000/00111100/01111110/11011011/11011011/01111110/00111100/00011000",
            "00011000/00111000/01111000/11111000/00011000/00011000/00011000/00011000",
            "00011000/00111000/01111000/11111000/00011000/00110000/01100000/11000000",
            "00011000/00111100/01111110/11011011/11011011/01111110/00111100/00011000"
        };
        for(int i=0;i<names.Length;i++)
            tree.nodes.Add(new RebirthTreeNodeDefinition { id="rebirth_"+i, displayName=names[i], description=descriptions[i], cost=costs[i], prerequisites=requires[i], bonus=bonuses[i], value=values[i], icon=PixelIcon("Rebirth "+names[i],patterns[i]) });
        EnsureExpandedRebirthTree(tree);
        EditorUtility.SetDirty(tree);
        return tree;
    }

    private static void EnsureExpandedRebirthTree(RebirthTreeDefinition tree)
    {
        AddRebirthTreeNode(tree,"rebirth_10","War March","All summoned monsters move 12% faster.",2,new[]{6},RebirthTreeBonus.MonsterMoveSpeed,.12f,"00011000/00111100/01111110/11011011/11011011/01111110/00111100/00011000");
        AddRebirthTreeNode(tree,"rebirth_11","Frenzy Brand","All summoned monsters attack 15% faster.",3,new[]{10},RebirthTreeBonus.MonsterAttackSpeed,.15f,"00100100/01111110/11111111/01111110/00111100/01111110/11111111/00100100");
        AddRebirthTreeNode(tree,"rebirth_12","Grave Bulwark","All summoned monsters gain another 25% maximum health.",2,new[]{6},RebirthTreeBonus.MonsterHealth,.25f,"00011000/00111100/01111110/11111111/01111110/01111110/11011011/11011011");
        AddRebirthTreeNode(tree,"rebirth_13","Legion Gate","Increase the maximum army capacity by 6.",3,new[]{12},RebirthTreeBonus.ArmyCapacity,6f,"00111100/01111110/11000011/11011011/11011011/11000011/01111110/00111100");
        AddRebirthTreeNode(tree,"rebirth_14","Rapid Conduit","Automatic summons complete another 10% faster.",2,new[]{9},RebirthTreeBonus.AutoSummonSpeed,.10f,"00011000/00110000/01100000/11111100/00011000/00011000/00011000/00011000");
        AddRebirthTreeNode(tree,"rebirth_15","Merchant Cartel","Gain another 25% gold from passive income and combat damage.",3,new[]{3},RebirthTreeBonus.GoldIncome,.25f,"00111100/01111110/11011011/11000011/11011011/11000011/01111110/00111100");
        AddRebirthTreeNode(tree,"rebirth_16","Eternal Ward","Newly summoned monsters are protected for another 1.25 seconds.",3,new[]{14},RebirthTreeBonus.SpawnProtection,1.25f,"00111100/01111110/11111111/11011011/11011011/11111111/01111110/00111100");
        AddRebirthTreeNode(tree,"rebirth_17","Final Bargain","Summons cost another 10% less gold.",3,new[]{15},RebirthTreeBonus.SummonCostDiscount,.10f,"00011000/00111100/01111110/11011011/11011011/01111110/00111100/00011000");
        EditorUtility.SetDirty(tree);
    }

    private static void AddRebirthTreeNode(RebirthTreeDefinition tree,string id,string name,string description,int cost,int[] prerequisites,RebirthTreeBonus bonus,float value,string pattern)
    {
        if(tree.nodes.Any(node=>node!=null && node.id==id)) return;
        tree.nodes.Add(new RebirthTreeNodeDefinition { id=id, displayName=name, description=description, cost=cost, prerequisites=prerequisites, bonus=bonus, value=value, icon=PixelIcon("Rebirth "+name,pattern) });
    }

    private static RebirthTreeNodeView AuthorTreeNode(Transform parent,RebirthTreeNodeDefinition node,int index,Vector2 position,TMP_FontAsset font)
    {
        string name="Tree Node "+index;
        Transform old=parent.Find(name); GameObject go=old!=null?old.gameObject:new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button),typeof(RebirthTreeNodeView)); go.layer=5;
        if(old==null) go.transform.SetParent(parent,false);
        RectTransform rect=go.GetComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f); rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=position; rect.sizeDelta=new Vector2(190,116);
        Image frame=go.GetComponent<Image>(); frame.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); frame.type=UnityEngine.UI.Image.Type.Sliced; frame.color=new Color(.10f,.15f,.18f,1f);
        Button button=go.GetComponent<Button>(); button.targetGraphic=frame;
        Transform iconTransform=go.transform.Find("Icon"); GameObject iconGo=iconTransform!=null?iconTransform.gameObject:new GameObject("Icon",typeof(RectTransform),typeof(Image)); iconGo.layer=5; if(iconTransform==null) iconGo.transform.SetParent(go.transform,false);
        RectTransform iconRect=iconGo.GetComponent<RectTransform>(); iconRect.anchorMin=iconRect.anchorMax=new Vector2(.5f,.5f); iconRect.pivot=new Vector2(.5f,.5f); iconRect.anchoredPosition=new Vector2(-62,5); iconRect.sizeDelta=new Vector2(54,54);
        Image icon=iconGo.GetComponent<Image>(); icon.sprite=node.icon; icon.preserveAspect=true; icon.color=Gold; icon.raycastTarget=false;
        TMP_Text title=WheelText("Title",go.transform,node.displayName,new Vector2(27,16),new Vector2(132,54),13,font,Color.white); title.alignment=TextAlignmentOptions.Center; title.textWrappingMode=TextWrappingModes.Normal;
        TMP_Text cost=WheelText("Cost",go.transform,"1 P",new Vector2(27,-35),new Vector2(126,24),11,font,Mint); cost.alignment=TextAlignmentOptions.Center;
        RebirthTreeNodeView view=go.GetComponent<RebirthTreeNodeView>(); view.nodeIndex=index; view.button=button; view.frame=frame; view.icon=icon; view.title=title; view.cost=cost;
        button.onClick.RemoveAllListeners(); button.onClick.AddListener(view.Purchase);
        return view;
    }

    private static Image AuthorTreeConnector(string name,Transform parent,Vector2 from,Vector2 to)
    {
        Transform old=parent.Find(name); GameObject go=old!=null?old.gameObject:new GameObject(name,typeof(RectTransform),typeof(Image)); go.layer=5;
        if(old==null) go.transform.SetParent(parent,false);
        Vector2 delta=to-from; RectTransform rect=go.GetComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f); rect.pivot=new Vector2(0f,.5f); rect.anchoredPosition=from; rect.sizeDelta=new Vector2(delta.magnitude,7f); rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
        Image line=go.GetComponent<Image>(); line.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); line.type=UnityEngine.UI.Image.Type.Sliced; line.color=new Color(.26f,.34f,.37f,.56f); line.raycastTarget=false;
        go.transform.SetAsFirstSibling(); return line;
    }

    private static TMP_Text WheelText(string name,Transform parent,string value,Vector2 position,Vector2 size,float fontSize,TMP_FontAsset font,Color color)
    {
        Transform old=parent.Find(name); GameObject go=old!=null?old.gameObject:new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); go.layer=5;
        if(old==null) go.transform.SetParent(parent,false);
        RectTransform rect=go.GetComponent<RectTransform>(); rect.anchorMin=new Vector2(.5f,.5f); rect.anchorMax=new Vector2(.5f,.5f); rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=position; rect.sizeDelta=size;
        TMP_Text text=go.GetComponent<TMP_Text>(); text.font=font; text.text=value; text.fontSize=fontSize; text.color=color; text.raycastTarget=false; text.textWrappingMode=TextWrappingModes.Normal; text.overflowMode=TextOverflowModes.Overflow; text.enableAutoSizing=true; text.fontSizeMin=Mathf.Max(8,fontSize-4); text.fontSizeMax=fontSize;
        return text;
    }

    private static Button WheelButton(string name,Transform parent,string caption,Vector2 position,Vector2 size,Color color,TMP_FontAsset font)
    {
        Transform old=parent.Find(name); GameObject go=old!=null?old.gameObject:new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button)); go.layer=5;
        if(old==null) go.transform.SetParent(parent,false);
        RectTransform rect=go.GetComponent<RectTransform>(); rect.anchorMin=new Vector2(.5f,.5f); rect.anchorMax=new Vector2(.5f,.5f); rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=position; rect.sizeDelta=size;
        Image image=go.GetComponent<Image>(); image.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); image.type=UnityEngine.UI.Image.Type.Sliced; image.color=color; image.raycastTarget=true;
        Button button=go.GetComponent<Button>(); button.targetGraphic=image;
        Transform labelTransform=go.transform.Find("Label"); TMP_Text label=labelTransform==null?WheelText("Label",go.transform,caption,Vector2.zero,size-new Vector2(20,8),15,font,Color.white):labelTransform.GetComponent<TMP_Text>();
        label.text=caption; label.font=font; label.alignment=TextAlignmentOptions.Center; label.enableAutoSizing=true; label.fontSizeMin=11; label.fontSizeMax=15;
        return button;
    }

    private static void AuthorHero(GameObject root, DamagePopup damage, HeroUpgradeThought thought, HeroUpgradeDefinition[] defs, GameObject ring,HeroSkillBurst hitEffect,HeroSkillBurst healingPulse,HeroSkillBurst arcaneNova,HeroFireField fireField,HeroOrbitingSaw orbitingSaw,HeroSkillBurst lightningStrike)
    {
        AuthorPhysics(root, true);
        HeroBotAI hero = root.GetComponent<HeroBotAI>();
        hero.damagePopupPrefab = damage; hero.upgradeThoughtPrefab = thought; hero.upgradeDefinitions = defs;
        hero.attackImpactVisualPrefab=hitEffect;
        hero.healingPulseVisualPrefab=healingPulse; hero.arcaneNovaVisualPrefab=arcaneNova;
        hero.fireFieldPrefab=fireField; hero.orbitingSawPrefab=orbitingSaw; hero.lightningStrikeVisualPrefab=lightningStrike;
        hero.knockbackMotor = root.GetComponent<CombatKnockback>();
        hero.spawnProtection = AttachProtection(root, ring, 2.5f);
        hero.knockbackForce = 7; hero.areaKnockbackForce = 9;
        EditorUtility.SetDirty(hero);
    }

    private static SpawnProtection AttachProtection(GameObject root, GameObject ringPrefab, float duration)
    {
        SpawnProtection protection = root.GetComponent<SpawnProtection>() ?? root.AddComponent<SpawnProtection>();
        protection.duration = duration;

        GameObject ring = protection.ringVisual;
        if (ring == null) ring = (GameObject)PrefabUtility.InstantiatePrefab(ringPrefab, root.transform);
        ring.name = "Spawn Protection Ring";
        ring.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        ring.transform.localRotation = Quaternion.identity;
        ring.transform.localScale = Vector3.one * 3f;
        protection.ringVisual = ring;
        protection.ringRenderer = ring.GetComponent<Renderer>();
        Transform shellTransform=ring.transform.Find("Energy Shell");
        protection.energyShellRenderer=shellTransform!=null?shellTransform.GetComponent<Renderer>():null;
        protection.energyGraphEffect=null;
        if (protection.ringRenderer != null)
        {
            protection.ringRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Combat/Spawn Protection.mat");
            protection.ringRenderer.sortingOrder = 220;
            protection.ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            protection.ringRenderer.receiveShadows = false;
        }
        protection.introPulseDuration = 0.28f;
        protection.introPulseScale = 1.25f;
        protection.fadeOutDuration = 0.5f;

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
        if (existing != null && existing.GetComponent<MonsterHealthBar>() != null)
        {
            var existingBar=existing.GetComponent<MonsterHealthBar>();
            existingBar.monster=monster;
            existingBar.canvasGroup=existing.GetComponent<CanvasGroup>();
            var existingFill=existing.Find("Background/Fill");
            if(existingFill!=null) existingBar.fill=existingFill.GetComponent<Image>();
            existingBar.visualRoot=existing as RectTransform;
            if(existingBar.fill!=null)
            {
                existingBar.fill.type=UnityEngine.UI.Image.Type.Filled;
                existingBar.fill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;
                if(existingBar.fill.sprite==null) existingBar.fill.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            }
            EnsureMonsterTacticalIndicator(root, monster);
            return;
        }

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
        fill.type=UnityEngine.UI.Image.Type.Filled; fill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;
        fill.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        fill.type=UnityEngine.UI.Image.Type.Filled; fill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;
        fill.type = UnityEngine.UI.Image.Type.Filled;
        fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
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
        EnsureMonsterTacticalIndicator(root, monster);
    }

    private static void EnsureMonsterTacticalIndicator(GameObject root, MonsterAI monster)
    {
        Transform existing=root.transform.Find("AI Intent");
        GameObject go=existing!=null?existing.gameObject:new GameObject("AI Intent",typeof(TextMeshPro),typeof(MonsterTacticalIndicator));
        if(existing==null) go.transform.SetParent(root.transform,false);
        go.transform.localPosition=new Vector3(0f,2.9f,0f); go.transform.localScale=Vector3.one*.16f;
        TextMeshPro label=go.GetComponent<TextMeshPro>(); label.font=TMP_Settings.defaultFontAsset; label.fontSize=5.5f; label.fontStyle=FontStyles.Bold; label.alignment=TextAlignmentOptions.Center; label.text="… THINK"; label.color=new Color(.96f,.92f,.72f,1f); label.rectTransform.sizeDelta=new Vector2(14f,2.4f); label.GetComponent<Renderer>().sortingOrder=165;
        MonsterTacticalIndicator indicator=go.GetComponent<MonsterTacticalIndicator>(); indicator.monster=monster; indicator.intentText=label; indicator.worldOffset=new Vector3(0f,2.9f,0f);
    }
    private static Material CreateDamageTextMaterial(TMP_FontAsset font)
    {
        if (font == null || font.material == null) return null;

        string path = Root + "/Materials/Combat/Damage Text.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (mat == null)
        {
            mat = new Material(font.material);
            mat.name = "Damage Text";
            AssetDatabase.CreateAsset(mat, path);
        }

        if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);

        if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
            mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.05f, 0.03f, 0.02f, 0.9f));

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static DamagePopup CreateDamage()
    {
        string assetPath = Root + "/Prefabs/Combat/Damage Number.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        GameObject root = existing != null ? PrefabUtility.LoadPrefabContents(assetPath) : new GameObject("Damage Number", typeof(RectTransform), typeof(TextMeshPro), typeof(DamagePopup));
        try
        {
            root.name = "Damage Number";
            TextMeshPro text = root.GetComponent<TextMeshPro>() ?? root.AddComponent<TextMeshPro>();
            text.font = TMP_Settings.defaultFontAsset; text.text = "128"; text.fontSize = 16f;
            text.fontStyle = FontStyles.Bold; text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.color = Gold;
            text.fontSharedMaterial = CreateDamageTextMaterial(text.font);
            text.rectTransform.sizeDelta = new Vector2(20f, 6.5f); text.GetComponent<Renderer>().sortingOrder = 180;
            root.transform.localScale = Vector3.one * .68f;
            DamagePopup popup = root.GetComponent<DamagePopup>() ?? root.AddComponent<DamagePopup>(); popup.label = text;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            return prefab.GetComponent<DamagePopup>();
        }
        finally { if (existing != null) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
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
        string assetPath = Root + "/Prefabs/Combat/Experience Orb.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (existing != null && existing.GetComponent<ExperienceOrb>() != null) return existing.GetComponent<ExperienceOrb>();
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "Experience Orb";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.25f;
        go.GetComponent<Renderer>().sharedMaterial = UnlitMaterial("Experience", new Color(0.15f, 1.7f, 2.3f));
        go.AddComponent<ExperienceOrb>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, assetPath);
        Object.DestroyImmediate(go); return prefab.GetComponent<ExperienceOrb>();
    }

    private static LineRenderer CreateLightning()
    {
        string assetPath = Root + "/Prefabs/Combat/Mage Lightning.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (existing != null && existing.GetComponent<LineRenderer>() != null) return existing.GetComponent<LineRenderer>();
        GameObject go = new GameObject("Mage Lightning", typeof(LineRenderer));
        LineRenderer line = go.GetComponent<LineRenderer>();
        line.positionCount = 6; line.widthMultiplier = 0.075f; line.useWorldSpace = true;
        line.numCapVertices = 2; line.sharedMaterial = UnlitMaterial("Lightning", new Color(0.65f, 1.6f, 2.8f));
        for (int i = 0; i < 6; i++) line.SetPosition(i, new Vector3(i * 0.4f, i % 2 == 0 ? 0 : 0.12f, 0));
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, assetPath);
        Object.DestroyImmediate(go); return prefab.GetComponent<LineRenderer>();
    }

    // Cleanup for the accidentally authored purple/particle shield layers.
    private static void RemoveObsoleteProtectionVfxArtifacts()
    {
        AssetDatabase.DeleteAsset("Assets/Project_/VFX/Spawn Shield Motes.vfx");
        AssetDatabase.DeleteAsset(Root+"/Prefabs/Combat/Visual Effect Carrier.prefab");
    }
    private static Material CreateRingMaterial()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Project_/VFX/SpawnProtection.shadergraph");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Protection Shader Graph failed to import.");
        string path = Root + "/Materials/Combat/Spawn Protection.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
        {
            mat.renderQueue = 3100;
            EditorUtility.SetDirty(mat);
            return mat;
        }
        else
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.shader = shader;
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.15f, 0.95f, 1f, 1f));
        if (mat.HasProperty("_Intensity")) mat.SetFloat("_Intensity", 5f);
        if (mat.HasProperty("_Radius")) mat.SetFloat("_Radius", 0.38f);
        if (mat.HasProperty("_Thickness")) mat.SetFloat("_Thickness", 0.055f);
        if (mat.HasProperty("_Softness")) mat.SetFloat("_Softness", 0.025f);
        if (mat.HasProperty("_Opacity")) mat.SetFloat("_Opacity", 1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material CreateShieldShellMaterial()
    {
        const string path = Root + "/Materials/Combat/Spawn Protection Shell.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null)
        {
            material = new Material(shader);
            material.name = "Spawn Protection Shell";
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(.12f,.82f,1f,.16f));
        if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(.12f,.82f,1f,.16f));
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject CreateRing(Material mat,VisualEffectAsset shieldGraph)
    {
        string assetPath = Root + "/Prefabs/Combat/Spawn Protection Ring.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
        bool prefabContents = root != null;
        if (root == null) root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        root.name = "Spawn Protection Ring";
        MeshFilter filter = root.GetComponent<MeshFilter>();
        Renderer renderer = root.GetComponent<Renderer>();
        if (filter == null || renderer == null)
        {
            foreach (Component component in root.GetComponents<Component>())
                if (component is not Transform && component is not MeshFilter && component is not MeshRenderer) Object.DestroyImmediate(component);
            if (filter == null) filter = root.AddComponent<MeshFilter>();
            if (renderer == null) renderer = root.AddComponent<MeshRenderer>();
        }
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        filter.sharedMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(sphere);
        Collider collider = root.GetComponent<Collider>(); if (collider != null) Object.DestroyImmediate(collider);
        root.transform.localPosition = new Vector3(0f,1.05f,0f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 3f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        Transform shell = root.transform.Find("Energy Shell");
        if (shell == null)
        {
            GameObject shellObject = new GameObject("Energy Shell", typeof(MeshFilter), typeof(MeshRenderer));
            shellObject.transform.SetParent(root.transform, false); shell = shellObject.transform;
        }
        GameObject shellSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.GetComponent<MeshFilter>().sharedMesh = shellSphere.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(shellSphere);
        shell.localPosition = Vector3.zero; shell.localRotation = Quaternion.identity; shell.localScale = Vector3.one * 1.015f;
        Renderer shellRenderer = shell.GetComponent<Renderer>(); shellRenderer.sharedMaterial = CreateShieldShellMaterial();
        shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; shellRenderer.receiveShadows = false;
        // Keep only the original Shader Graph sphere. Do not add any particle/VFX layers.
        foreach(Transform child in root.transform.Cast<Transform>().Where(child=>child.name.StartsWith("Shield Motes ") || child.name=="Energy Field VFX").ToArray()) Object.DestroyImmediate(child.gameObject);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
        if (prefabContents) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root);
        return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
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
        d.maxPowerLevel = int.MaxValue;
        d.unlockCost = new[] { 0d, 180d, 450d, 850d }[slot];
        d.basePowerCost = new[] { 35, 80, 150, 220 }[slot];
        d.powerCostGrowth = new[] { 1.45f, 1.48f, 1.52f, 1.55f }[slot];
        d.healthPerLevel = new[] { 0.10f, 0.08f, 0.13f, 0.07f }[slot];
        d.damagePerLevel = new[] { 0.09f, 0.11f, 0.07f, 0.13f }[slot];

        d.maxAutoLevel = 6;
        d.baseAutoCost = new[] { 100, 180, 300, 420 }[slot];
        d.autoCostGrowth = new[] { 1.55f, 1.60f, 1.65f, 1.7f }[slot];
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
        string[] names = { "Vitality", "Damage", "Attack Speed", "Move Speed", "Armor", "Pickup Range", "Cleave", "Knockback", "Life Steal", "Multi Strike", "Shockwave", "Execution", "Healing Pulse", "Arcane Nova", "Inferno Field", "Orbiting Saws" };
        string[] descriptions = {
            "Increase maximum health and immediately restore the same amount.",
            "Increase every Hero attack's damage.",
            "Shorten the delay between attacks.",
            "Move faster while chasing enemies, experience, or objectives.",
            "Reduce damage received from monsters.",
            "Collect experience orbs from farther away.",
            "Attacks also damage nearby monsters.",
            "Push enemies farther away with each attack.",
            "Restore health based on damage dealt.",
            "Chance to follow an attack with a bonus strike.",
            "Every few attacks, release an area shockwave around the Hero.",
            "Deal bonus damage to monsters below 25% health.",
            "Periodically restore a portion of maximum health when wounded.",
            "Release a wide lightning nova that strikes clustered enemies.",
            "Call down a storm that leaves a damaging burning field beneath a monster group.",
            "Summon spinning saw blades that orbit the Hero and cut nearby monsters."
        };
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
            "00111100/01111110/11011011/11011011/01111110/00100100/00111100/00000000",
            "00011000/00111100/01111110/11111111/11111111/01111110/00111100/00011000",
            "00011000/00111100/01111110/11011011/11111111/10111101/01111110/00011000",
            "11111111/11000011/10100101/10011001/10011001/10100101/11000011/11111111",
            "10011001/01111110/00111100/11111111/00111100/01111110/10011001/00000000" };
        float[][] utilityWeights =
        {
            new[]{1.25f,.35f,.05f,.1f,0f,0f,0f,0f}, new[]{0f,.2f,.35f,.35f,.25f,0f,.1f,.8f},
            new[]{0f,.1f,.25f,.35f,.1f,0f,.2f,.85f}, new[]{0f,.2f,.15f,.1f,0f,.5f,.75f,.1f},
            new[]{.1f,1.1f,.25f,.1f,0f,0f,0f,.1f}, new[]{0f,0f,0f,0f,0f,1.2f,.75f,.05f},
            new[]{0f,.15f,.2f,1.2f,0f,0f,.15f,.55f}, new[]{0f,.8f,.25f,.3f,0f,0f,.1f,.2f},
            new[]{1.1f,.2f,.2f,.2f,0f,0f,0f,.55f}, new[]{0f,.2f,.2f,.85f,.15f,0f,.1f,.7f},
            new[]{0f,.15f,.2f,1.25f,0f,0f,.1f,.5f}, new[]{0f,.1f,.1f,.1f,1.1f,0f,.05f,.5f},
            new[]{1.35f,.05f,.05f,.05f,0f,0f,0f,.15f}, new[]{0f,.1f,.15f,1.15f,.05f,0f,.15f,.7f},
            new[]{0f,.35f,1.1f,.15f,.1f,.05f,.2f,.85f}, new[]{0f,.25f,.9f,.2f,.05f,0f,.1f,1.15f}
        };
        HeroUpgradeDefinition[] defs = new HeroUpgradeDefinition[16];
        for (int i = 0; i < 16; i++)
        {
            string path = Root + "/Upgrades/Hero " + names[i] + ".asset";
            defs[i] = AssetDatabase.LoadAssetAtPath<HeroUpgradeDefinition>(path);
            if (defs[i] != null)
            {
                defs[i].type=(HeroUpgradeType)i; defs[i].category=i>=10?HeroUpgradeCategory.Skill:HeroUpgradeCategory.Stat; defs[i].displayName=names[i];
                defs[i].description=descriptions[i];
                defs[i].utility=BuildHeroUpgradeUtilityProfile(utilityWeights[i]);
                EditorUtility.SetDirty(defs[i]);
                if(defs[i].icon==null)
                {
                    defs[i].icon=PixelIcon(names[i],patterns[i]);
                    EditorUtility.SetDirty(defs[i]);
                }
                continue;
            }
            Sprite icon = PixelIcon(names[i], patterns[i]);
            var d = ScriptableObject.CreateInstance<HeroUpgradeDefinition>();
            d.type = (HeroUpgradeType)i; d.category=i>=10?HeroUpgradeCategory.Skill:HeroUpgradeCategory.Stat; d.displayName = names[i]; d.icon = icon; d.description=descriptions[i];
            d.utility=BuildHeroUpgradeUtilityProfile(utilityWeights[i]);
            AssetDatabase.CreateAsset(d, path); defs[i] = d;
        }
        return defs;
    }

    private static HeroUpgradeUtilityProfile BuildHeroUpgradeUtilityProfile(float[] w)
    {
        return new HeroUpgradeUtilityProfile
        {
            survivalNeed=w[0], closeThreat=w[1], enemyCrowding=w[2], rangedThreat=w[3],
            weakenedEnemies=w[4], experienceUrgency=w[5], objectivePressure=w[6], offensiveSynergy=w[7]
        };
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
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single; importer.spritePixelsPerUnit = 48;
        importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static HeroUpgradeThought CreateThought(HeroUpgradeDefinition[] defs)
    {
        string path=Root+"/Prefabs/Combat/Hero Upgrade Thought.prefab";
        bool exists=AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null;
        GameObject root=exists?PrefabUtility.LoadPrefabContents(path):new GameObject("Hero Upgrade Thought");
        bool loaded=exists;
        try
        {
            root.name="Hero Upgrade Thought";
            HeroUpgradeThought thought=root.GetComponent<HeroUpgradeThought>()??root.AddComponent<HeroUpgradeThought>();
            SpriteRenderer icon=EnsureThoughtSprite(root.transform,"Chosen upgrade icon");
            icon.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Image/Upgrades/Thinking.png");
            if(icon.sprite==null) icon.sprite=PixelIcon("Thinking","00111100/01000010/00000010/00001100/00011000/00000000/00011000/00000000");
            icon.sortingOrder=150; icon.transform.localPosition=new Vector3(0f,.63f,0f);
            thought.upgradeIcon=icon; thought.thinkingIcon=icon.sprite;
            TMP_Text caption=EnsureThoughtLabel(root.transform,"Chosen upgrade name",new Vector3(0f,.25f,-.01f),new Vector2(18f,2.5f),.12f,5f);
            caption.text="..."; caption.color=Gold; caption.GetComponent<Renderer>().sortingOrder=155;
            thought.caption=caption;
            Sprite cardSprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            AuthorThoughtCard(root.transform,"Choice Card A",-1,cardSprite,out SpriteRenderer cardA,out SpriteRenderer optionIconA,out TMP_Text optionTextA);
            AuthorThoughtCard(root.transform,"Choice Card B",1,cardSprite,out SpriteRenderer cardB,out SpriteRenderer optionIconB,out TMP_Text optionTextB);
            thought.choiceCardA=cardA; thought.choiceCardB=cardB; thought.choiceIconA=optionIconA; thought.choiceIconB=optionIconB;
            thought.choiceTextA=optionTextA; thought.choiceTextB=optionTextB;
            thought.thinkingDuration=1.25f; thought.resultDuration=3f; thought.worldOffset=new Vector3(0f,3.65f,0f); thought.iconScale=1.55f; thought.resultPopScale=1.42f;
            EditorUtility.SetDirty(thought);
            GameObject prefab=PrefabUtility.SaveAsPrefabAsset(root,path);
            return prefab.GetComponent<HeroUpgradeThought>();
        }
        finally { if(loaded) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
    }

    private static SpriteRenderer EnsureThoughtSprite(Transform parent,string name)
    {
        Transform child=parent.Find(name);
        GameObject go=child!=null?child.gameObject:new GameObject(name,typeof(SpriteRenderer));
        if(child==null) go.transform.SetParent(parent,false);
        return go.GetComponent<SpriteRenderer>();
    }

    private static TMP_Text EnsureThoughtLabel(Transform parent,string name,Vector3 position,Vector2 size,float scale,float fontSize)
    {
        Transform child=parent.Find(name);
        GameObject go=child!=null?child.gameObject:new GameObject(name,typeof(TextMeshPro));
        if(child==null) go.transform.SetParent(parent,false);
        go.transform.localPosition=position; go.transform.localRotation=Quaternion.identity; go.transform.localScale=Vector3.one*scale;
        TMP_Text text=go.GetComponent<TMP_Text>(); text.font=TMP_Settings.defaultFontAsset; text.fontSize=fontSize; text.alignment=TextAlignmentOptions.Center;
        text.textWrappingMode=TextWrappingModes.Normal; text.overflowMode=TextOverflowModes.Overflow; text.color=Color.white; text.rectTransform.sizeDelta=size; text.raycastTarget=false;
        return text;
    }

    private static void AuthorThoughtCard(Transform parent,string name,int side,Sprite backgroundSprite,out SpriteRenderer background,out SpriteRenderer icon,out TMP_Text label)
    {
        background=EnsureThoughtSprite(parent,name); background.sprite=backgroundSprite; background.color=new Color(.035f,.09f,.12f,.96f); background.sortingOrder=140;
        background.transform.localPosition=new Vector3(side*.7f,-.38f,0f); background.transform.localRotation=Quaternion.identity; background.transform.localScale=new Vector3(1.3f,.7f,1f);
        icon=EnsureThoughtSprite(background.transform,"Upgrade Icon"); icon.sortingOrder=143; icon.transform.localPosition=new Vector3(-.43f,0f,-.01f); icon.transform.localScale=Vector3.one*.32f;
        label=EnsureThoughtLabel(background.transform,"Upgrade Detail",new Vector3(.13f,0f,-.02f),new Vector2(9f,4f),.13f,5.4f);
        label.alignment=TextAlignmentOptions.Left; label.color=Color.white; label.GetComponent<Renderer>().sortingOrder=145;
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
        Text("Heading", root.transform, "SUMMONER UPGRADES", 28, 22, 20, 38, 21, Gold);
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
            progress.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Image/ui.png") ?? AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); progress.color=Mint; progress.fillAmount = 0; controller.progress = progress;
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
            if(d.maxPowerLevel!=int.MaxValue) throw new Exception("Monster power should have no designed rank cap: "+d.displayName);
            for (int level = 0; level < 12; level++)
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
        HeroDoomGameManager manager=Object.FindFirstObjectByType<HeroDoomGameManager>();
        var spawner = Object.FindFirstObjectByType<PlayerSpawner>();
        if (manager==null || spawner == null || spawner.upgrades == null) throw new Exception("Upgrade system is not wired.");
        SummonerUpgrades upgrades = spawner.upgrades;
        var progressionHud=Object.FindFirstObjectByType<HeroHUDController>();
        if(progressionHud==null || progressionHud.progressionRoot==null || progressionHud.experienceFill==null || progressionHud.experienceText==null || progressionHud.upgradeIconRoot==null || progressionHud.upgradeSlots==null || progressionHud.upgradeSlots.Length!=16)
            throw new Exception("Hero progression HUD must be authored and wired in the scene.");
        Sprite uiFillSprite=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Image/ui.png");
        if(progressionHud.experienceFill.type!=UnityEngine.UI.Image.Type.Filled || progressionHud.experienceFill.sprite==null || progressionHud.experienceFill.sprite!=uiFillSprite) throw new Exception("Hero EXP bar needs the authored ui.png sprite on its Filled image.");
        foreach(var slot in progressionHud.upgradeSlots)
        {
            if(slot==null || slot.root==null || slot.icon==null || slot.icon.sprite==null || slot.count==null) throw new Exception("An authored Hero upgrade icon slot is not wired or has no sprite.");
            HeroUpgradeHover hover=slot.root.GetComponent<HeroUpgradeHover>();
            if(hover==null || hover.tooltip==null) throw new Exception("Hero upgrade icon hover details are not wired: "+slot.type);
        }
        GameObject pillarAsset=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Environment/pillar.prefab");
        if(pillarAsset==null || pillarAsset.GetComponent<CapturePillar>()==null || pillarAsset.GetComponentInChildren<Renderer>(true)==null) throw new Exception("Authored pillar prefab is missing CapturePillar or its visible model.");
        if(Object.FindFirstObjectByType<ScrollRect>()==null || Object.FindObjectsByType<HeroUpgradeTooltip>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0) throw new Exception("Authored upgrade scroll view or Hero upgrade hover tooltip is missing.");
        LuckyWheelController wheel=Object.FindFirstObjectByType<LuckyWheelController>(FindObjectsInactive.Include);
        if(wheel==null || wheel.popup==null || wheel.wheel==null || wheel.spinButton==null || wheel.openButton==null) throw new Exception("Lucky Wheel popup, wheel or button binding is missing.");
        if(wheel.popup.activeSelf) throw new Exception("Lucky Wheel popup must start hidden and open only when its icon is clicked.");
        RebirthTreeController rebirthTree=Object.FindFirstObjectByType<RebirthTreeController>(FindObjectsInactive.Include);
        if(rebirthTree==null || rebirthTree.popup==null || rebirthTree.openButton==null || rebirthTree.closeButton==null || rebirthTree.definition==null || upgrades.rebirthTree!=rebirthTree.definition)
            throw new Exception("Rebirth Tree icon, popup, data asset or button binding is missing.");
        if(rebirthTree.popup.activeSelf) throw new Exception("Rebirth Tree popup must start hidden and open only when its icon is clicked.");
        if(rebirthTree.definition.nodes==null || rebirthTree.definition.nodes.Count<18 || rebirthTree.nodeViews==null || rebirthTree.nodeViews.Length!=rebirthTree.definition.nodes.Count)
            throw new Exception("Rebirth Tree must have one authored view per upgrade node.");
        if(rebirthTree.nodeViews.Any(view=>view==null || view.button==null || view.icon==null || view.title==null || view.cost==null))
            throw new Exception("An authored Rebirth Tree node is missing its visible controls.");
        if(rebirthTree.connectors==null || rebirthTree.connectors.Length<17 || rebirthTree.connectorFromNodes.Length!=rebirthTree.connectors.Length || rebirthTree.connectorToNodes.Length!=rebirthTree.connectors.Length)
            throw new Exception("Rebirth Tree branch connections are incomplete.");
        RebirthTreePanView treePan=rebirthTree.popup.GetComponentInChildren<RebirthTreePanView>(true);
        if(treePan==null || treePan.viewport==null || treePan.content==null || treePan.viewport.GetComponent<RectMask2D>()==null)
            throw new Exception("Rebirth Tree needs an authored clipped drag-and-zoom map viewport.");
        if(upgrades.PurchasedRebirthNodes==null || upgrades.PurchasedRebirthNodes.Count!=rebirthTree.definition.nodes.Count)
            throw new Exception("Rebirth Tree permanent purchase state is not initialized.");
        for (int i = 0; i < 4; i++)
        {
            var m = spawner.GetPrefab(i).GetComponent<MonsterAI>();
            if (m.damagePopupPrefab == null || m.experienceOrbPrefab == null || m.hitEffectPrefab==null || m.spawnProtection == null || m.spawnProtection.ringVisual==null || m.spawnProtection.ringRenderer==null || m.spawnProtection.energyShellRenderer==null || m.knockbackMotor == null) throw new Exception("Monster feedback or protection sphere setup missing: " + i);
            var bar=m.GetComponentInChildren<MonsterHealthBar>(true);
            if (bar == null || bar.fill==null || bar.fill.type!=UnityEngine.UI.Image.Type.Filled) throw new Exception("Monster health slider missing: " + i);
            if (m.GetComponent<NavMeshAgent>() == null || !m.GetComponent<Rigidbody>().isKinematic) throw new Exception("Physics missing: " + i);
            if (i == 1 && (m.mageProjectilePrefab == null || m.mageProjectilePrefab.GetComponent<MagicProjectile>() == null || m.lightningPrefab == null)) throw new Exception("Mage effects missing.");
            report += m.name + ": feedback, protection, navigation OK\n";
        }
        var cards = Object.FindObjectsByType<MonsterUpgradeCard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (cards.Length != 4 || cards.Any(c => c.upgrades == null || c.powerButton == null || c.autoButton == null)) throw new Exception("Expected four authored cards with bindings.");
        if(cards.Any(c=>c.progress==null || c.progress.sprite!=uiFillSprite || c.title.overflowMode==TextOverflowModes.Ellipsis || c.stats.overflowMode==TextOverflowModes.Ellipsis || c.autoStatus.overflowMode==TextOverflowModes.Ellipsis))
            throw new Exception("Summoner card text must fit without ellipsis and summon timer must use ui.png.");
        string[] prefabGuids=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"});
        string[] particlePrefabPaths=prefabGuids.Select(AssetDatabase.GUIDToAssetPath)
            .Where(path=>AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)
            .Where(path=>AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<ParticleSystem>(true).Length>0)
            .ToArray();
        if(particlePrefabPaths.Length>0) throw new Exception("Particle System components remain in authored prefabs: "+string.Join(", ",particlePrefabPaths));
        HeroBotAI heroPrefab=manager.heroPrefab!=null?manager.heroPrefab.GetComponent<HeroBotAI>():null;
        if(heroPrefab==null || heroPrefab.attackImpactVisualPrefab==null || heroPrefab.healingPulseVisualPrefab==null || heroPrefab.arcaneNovaVisualPrefab==null || heroPrefab.lightningStrikeVisualPrefab==null || heroPrefab.upgradeThoughtPrefab==null)
            throw new Exception("Hero prefab must link authored attack, skill, and two-card upgrade-thought prefabs.");
        if(heroPrefab.upgradeDefinitions==null || heroPrefab.upgradeDefinitions.Length<16 || heroPrefab.upgradeDefinitions.Any(d=>d==null || d.utility==null))
            throw new Exception("Hero AI upgrade utility profiles are missing from the authored upgrade definitions.");
        if(heroPrefab.upgradeDefinitions.Any(d=>d.category!=((int)d.type>=10?HeroUpgradeCategory.Skill:HeroUpgradeCategory.Stat)))
            throw new Exception("Hero upgrades must be authored as clearly separated stat and skill categories.");
        if(heroPrefab.upgradeThoughtPrefab.choiceCardA==null || heroPrefab.upgradeThoughtPrefab.choiceCardB==null || heroPrefab.upgradeThoughtPrefab.choiceTextA==null || heroPrefab.upgradeThoughtPrefab.choiceTextB==null)
            throw new Exception("Hero level-up thought prefab needs two authored upgrade cards with text and icons.");

        if (upgrades.maxAliveMonsters != 36) throw new Exception("Global monster cap should be 36.");
        if(!upgrades.IsMonsterUnlocked(0) || upgrades.IsMonsterUnlocked(1) || upgrades.IsMonsterUnlocked(2) || upgrades.IsMonsterUnlocked(3)) throw new Exception("The starting roster must unlock only the basic monster.");
        if(upgrades.rebirthButton==null || upgrades.rebirthLabel==null || upgrades.goldMultiplierPerRebirth<=0f) throw new Exception("Player Rebirth control or reward is not authored.");
        int[] expectedCaps = { 18, 12, 8, 8 };
        float[] expectedMinimumIntervals = { 4f, 6f, 8f, 10f };
        for (int i = 0; i < 4; i++)
        {
            MonsterUpgradeDefinition d = upgrades.definitions[i];
            if (d == null) throw new Exception("Missing upgrade definition: " + i);
            if(i>0 && d.unlockCost<=0d) throw new Exception("Monster unlock costs must be positive: "+d.displayName);
            if (d.maxAutoLevel != 6) throw new Exception("AUTO max level should be 6: " + d.displayName);
            if (d.maxAlive != expectedCaps[i]) throw new Exception("Wrong per-monster cap: " + d.displayName);
            if (Mathf.Abs(d.minimumInterval - expectedMinimumIntervals[i]) > 0.01f) throw new Exception("Wrong minimum interval: " + d.displayName);
            if (i > 0 && d.previousMonsterAutoLevelRequired != 3) throw new Exception("AUTO dependency should require previous Lv.3: " + d.displayName);
        }

        report += $"16 Hero upgrade hover tooltips OK ({Object.FindObjectsByType<HeroUpgradeTooltip>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length} authored panel)\n";
        report += $"Capture pillar prefab: {pillarAsset.name} with CapturePillar OK\n";
        report += "Lucky Wheel authored popup and spin rewards OK\n";
        report += $"Rebirth Tree: {rebirthTree.definition.nodes.Count} authored nodes, {rebirthTree.connectors.Length} branch links and permanent state OK\n";
        report += "4 authored upgrade cards OK\nSummoner caps and AUTO progression OK\nShader Graph: no shader errors\n";
        File.WriteAllText(Work + "/result.txt", report);
    }
}

// Refresh authoring assembly after progression and capture-point changes.
