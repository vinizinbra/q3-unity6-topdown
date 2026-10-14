using System.Collections.Generic;
using Photon.Deterministic;
using Quantum;
using Quantum.Editor;
using QuantumUser.View.Util;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the Training Zone course (movement + aim tutorial) into TrainingZoneScene - see
// docs/training-zone.md. The whole layout lives in Build() below as plain code so tweaking a gap or
// moving a sign is an edit + re-run, not hand-dragging cubes: the "TrainingZone" root is torn down and
// regenerated each run, then the Quantum map is re-baked.
//
// The course is a row of station chunks along +X (the gameplay camera looks +Z, so the course reads
// left -> right on screen). Each station is its own Chunk map entity with its own RespawnPoint, so a
// fall respawns the player at the start of the station they fell in (FallRespawnUtility picks the
// nearest chunk), not back at the beginning. Cubes are clones of the TraversalChunk prefab's own
// VisualCubes (BoxCollider + ChunkWallCube + TilesetPlatformBuilder), so tileset visuals build at
// runtime exactly like a generated chunk, and ChunkCompoundColliderBuilder bakes them into each
// station's Quantum collider.
//
// Nothing here may require the manual Jump button - Jump is keyboard-only (Space, see docs/input.md),
// so every height change is a 1-unit step (auto-mantle, MovementDataAsset.MaxLedgeHeight) and every
// gap is either an auto-hop gap or a Dash gap.
public static class TrainingZoneBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/TrainingZoneScene.unity";
    private const string RootName = "TrainingZone";
    private const string TemplateChunkPath = "Assets/_QuantumUser/Entities/LevelChunk/TraversalChunk.prefab";
    private const string DummyEnemyDataPath = "Assets/_QuantumUser/Resources/Enemy/Training/TrainingDummy.asset";
    private const string FinishPoiPath = "Assets/_QuantumUser/Entities/LevelChunk/TrainingFinishPoi.prefab";
    private const string FontPath = "Assets/3rd-party/Layer Lab/GUI Pro-SuperCasual/ResourcesData/Fonts/Inter_18pt-Medium SDF.asset";

    // Chunk min corners must sit on LevelConfig.CellSize (2) multiples, inside the generation grid
    // (origin 0,0 with no Boss Arena in the scene).
    private static readonly Vector3 Origin = new Vector3(8f, 0f, 8f);
    private const int Depth = 12;

    // Same floor convention as every LevelChunk prefab / WallChunk: cubes run y -3..1, so the walkable
    // floor is Y = 1 and anything at a different top is a ledge.
    private const float Bottom = -3f;
    private const float Floor = 1f;

    private const float TitleSize = 9f;
    private const float HintSize = 5f;
    // Rift Pink (#FD3971) - the project's signature accent.
    private static readonly Color TitleColor = new Color32(0xFD, 0x39, 0x71, 0xFF);
    private static readonly Color HintColor = Color.white;

    private const float DummyRespawnDelay = 2f;

    // Above AccessoryGuardConfig.MinDamageToBlock (5), so the guard always blocks it.
    private const float HazardDamage = 10f;

    // Plain floor between two lessons - see Station. Even, so chunk widths stay on CellSize (2).
    private const int BreathingRoom = 4;

    private static GameObject _floorTemplate;
    private static GameObject _blockTemplate;
    private static TMP_FontAsset _font;

    [MenuItem("Tools/RiftRaiders/Level/Build Training Zone")]
    public static void BuildMenu()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);

        if (scene.IsValid() == false || scene.isLoaded == false)
        {
            LogHelper.Error("TrainingZone", $"Open {ScenePath} first (it can be open additively).");
            return;
        }

        Build(scene);
    }

    public static void Build(Scene scene)
    {
        if (LoadTemplates() == false)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == RootName)
                Object.DestroyImmediate(root);
        }

        var zone = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(zone, scene);
        zone.transform.position = Origin;
        // Pops each station's text in as the player arrives + the Finish pad (View only).
        zone.AddComponent<TrainingZoneGuideWidget>();

        _stationRoots.Clear();
        var stations = _stationRoots;
        float x = 0f;

        BuildStart(zone.transform, ref x);
        BuildClimb(zone.transform, ref x);
        BuildTooHigh(zone.transform, ref x);
        BuildHopGaps(zone.transform, ref x);
        BuildDash(zone.transform, ref x);
        BuildHighGround(zone.transform, ref x);
        BuildPlatforms(zone.transform, ref x);
        BuildAccessory(zone.transform, ref x);
        BuildRange(zone.transform, ref x);
        BuildSkill(zone.transform, ref x);
        BuildFinish(zone.transform, ref x);

        Physics.SyncTransforms();

        foreach (GameObject station in stations)
        {
            station.GetComponent<ChunkCompoundColliderBuilder>().RebuildCompoundCollider();
            station.GetComponent<ChunkRespawnPointBaker>().BakeRespawnPoint();
            EditorUtility.SetDirty(station.GetComponent<QPrototypeChunk>());
            EditorUtility.SetDirty(station.GetComponent<QuantumEntityPrototype>());
        }

        EditorSceneManager.MarkSceneDirty(scene);

        QuantumMapData mapData = FindMapData(scene);

        if (mapData != null)
            QuantumEditorAutoBaker.BakeMap(mapData, QuantumMapDataBakeFlags.BakeMapData | QuantumMapDataBakeFlags.GenerateAssetDB);
        else
            LogHelper.Warn("TrainingZone", "No QuantumMapData in the scene - map not baked.");

        LogHelper.Log("TrainingZone", $"Built {stations.Count} stations, course length {x}.");
    }

    // ---------------------------------------------------------------- stations

    private static GameObject BuildStart(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "0_Start", ChunkType.LobbyStart, ref x, 12);
        Box(s, "Floor", 0, 0, 12, Depth, Floor, false);
        Respawn(s, 6, 6, Floor);

        Label(s, "TRAINING ZONE", 6, 9.5f, Floor, TitleSize * 1.2f, TitleColor);
        Label(s, "MOVE", 6, 3.2f, Floor, TitleSize, TitleColor);
        Label(s, "WASD  /  Left Stick", 6, 1.8f, Floor, HintSize, HintColor);
        Label(s, ">>>", 11, 6, Floor, TitleSize, TitleColor);
        return s;
    }

    // Auto-mantle: a 1-unit ledge is climbed just by walking into it.
    private static GameObject BuildClimb(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "1_Climb", ChunkType.Enemy, ref x, 16);
        Box(s, "Floor", 0, 0, 16, Depth, Floor, false);
        Box(s, "LowStep", 6, 3, 4, 6, Floor + 1, true);
        Respawn(s, 2, 6, Floor);

        Label(s, "CLIMB", 8, 1.9f, Floor, TitleSize, TitleColor);
        Label(s, "Walk into a low ledge", 8, 0.8f, Floor, HintSize, HintColor);
        return s;
    }

    // Auto-mantle only climbs 1 unit: a 2-unit wall is not climbable (not even with Jump). The way up
    // is the 1-unit side step in front of it, on the camera side so it's visible (floor -> step ->
    // wall top, 1 unit each). Text sits on the open floor to the left, well before the wall.
    private static GameObject BuildTooHigh(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "2_TooHigh", ChunkType.Enemy, ref x, 22);
        Box(s, "Floor", 0, 0, 22, Depth, Floor, false);
        Box(s, "TallWall", 11, 3, 6, 7, Floor + 2, true);
        Box(s, "SideStep", 14, 1, 3, 2, Floor + 1, true);
        // Second 2-unit wall right of the step (user layout) - no walking around on the floor, the
        // only way on is step -> wall.
        Box(s, "TallWall (1)", 17, 0, 3, 10, Floor + 2, true);
        Respawn(s, 2, 6, Floor);

        Label(s, "TOO HIGH?", 5, 8.6f, Floor, TitleSize, TitleColor);
        Label(s, "2 units up is a wall", 5, 7.4f, Floor, HintSize, HintColor);
        Label(s, "1 STEP AT A TIME", 5, 4.2f, Floor, TitleSize * 0.8f, TitleColor);
        Label(s, "Use the lower step", 5, 3.1f, Floor, HintSize, HintColor);
        return s;
    }

    // Auto-hop: walking off an edge with ground within reach hops across on its own.
    private static GameObject BuildHopGaps(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "3_HopGaps", ChunkType.Enemy, ref x, 16);
        Box(s, "Floor_A", 0, 0, 4, Depth, Floor, false);
        Box(s, "Floor_B", 6, 0, 3, Depth, Floor, false);
        Box(s, "Floor_C", 11, 0, 5, Depth, Floor, false);
        Respawn(s, 2, 6, Floor);

        Label(s, "GAPS", 2, 8.5f, Floor, TitleSize, TitleColor);
        Label(s, "Keep walking", 2, 7.2f, Floor, HintSize, HintColor);
        Label(s, "you hop over", 2, 6.2f, Floor, HintSize, HintColor);
        return s;
    }

    // Too wide to hop - Dash (no gravity while dashing, DashSkillData distance 5) carries across.
    private static GameObject BuildDash(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "4_Dash", ChunkType.Enemy, ref x, 16);
        Box(s, "Floor_A", 0, 0, 6, Depth, Floor, false);
        Box(s, "Floor_B", 10, 0, 6, Depth, Floor, false);
        Respawn(s, 2, 6, Floor);

        Label(s, "DASH", 3, 9, Floor, TitleSize, TitleColor);
        Label(s, "Shift  /  A", 3, 7.6f, Floor, HintSize, HintColor);
        Label(s, "Wide gaps:", 3, 4.6f, Floor, HintSize, HintColor);
        Label(s, "run and dash", 3, 3.6f, Floor, HintSize, HintColor);
        return s;
    }

    // A narrow raised walkway over the water: staircase up to a lookout, then a drop back down.
    private static GameObject BuildHighGround(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "5_HighGround", ChunkType.Enemy, ref x, 18);
        Box(s, "Floor_A", 0, 0, 4, Depth, Floor, false);
        Box(s, "Stair1", 3, 4, 4, 4, Floor + 1, true);
        // 1-unit gaps between the stairs (user layout).
        Box(s, "Stair2", 8, 4, 3, 4, Floor + 2, true);
        Box(s, "Lookout", 12, 3, 3, 6, Floor + 3, true);
        Box(s, "Floor_B", 14, 0, 4, Depth, Floor, false);
        Respawn(s, 2, 6, Floor);

        Label(s, "HIGH GROUND", 0, 10.2f, Floor, TitleSize * 0.8f, TitleColor);
        Label(s, "Step up, then", 0, 2.2f, Floor, HintSize, HintColor);
        Label(s, "drop down", 0, 1.2f, Floor, HintSize, HintColor);
        return s;
    }

    // Stepping-stone islands at mixed heights - hop + mantle chained together.
    private static GameObject BuildPlatforms(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "6_Platforms", ChunkType.Enemy, ref x, 20);
        Box(s, "Floor_A", 0, 0, 3, Depth, Floor, false);
        Box(s, "Stone1", 4, 4, 3, 3, Floor, true);
        Box(s, "Stone2", 8, 6, 3, 3, Floor + 1, true);
        Box(s, "Stone3", 12, 3, 3, 4, Floor, true);
        Box(s, "Stone4", 15, 7, 2, 3, Floor + 1, true);
        Box(s, "Floor_B", 17, 0, 3, Depth, Floor, false);
        Respawn(s, 1.5f, 6, Floor);

        Label(s, "PLATFORMS", 10, 1.6f, Floor, TitleSize, TitleColor, onGround: false);
        Label(s, "Hop from stone to stone", 10, 0.5f, Floor, HintSize, HintColor, onGround: false);
        return s;
    }

    // Accessory Guard lesson: a spike trap across the whole station (no way around) hits the player
    // once - the accessory blocks it and pops off (TeachingHit only hits a guard that can block, see
    // SpikeTrap.qtn) - then the text past the strip says to walk back for it.
    private static GameObject BuildAccessory(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "7_Accessory", ChunkType.Enemy, ref x, 20);
        Box(s, "Floor", 0, 0, 20, Depth, Floor, false);
        Respawn(s, 2, 6, Floor);

        Hazard(s, 7.75f, 0, 2f, Depth); // 2 m = exactly 2 socket columns (SpikeTrapView.socketSpacing 1)

        Label(s, "ACCESSORY", 3.5f, 8.6f, Floor, TitleSize, TitleColor);
        Label(s, "It blocks one hit", 3.5f, 7.4f, Floor, HintSize, HintColor);
        Label(s, "PICK IT BACK UP", 14.5f, 4.2f, Floor, TitleSize * 0.8f, TitleColor);
        Label(s, "Walk over your accessory", 14.5f, 3.1f, Floor, HintSize, HintColor);
        return s;
    }

    // Target dummies at mixed distances - weapons auto-fire at Aim.Target once in range.
    private static GameObject BuildRange(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "8_Range", ChunkType.Enemy, ref x, 24);
        Box(s, "Floor", 0, 0, 24, Depth, Floor, false);
        Box(s, "Cover1", 8, 2, 1, 3, Floor + 1, true);
        Box(s, "Cover2", 8, 8, 1, 3, Floor + 1, true);
        Respawn(s, 3, 6, Floor);

        Dummy(s, 13, 6);
        Dummy(s, 18, 9.5f);
        Dummy(s, 21, 3);

        Label(s, "AIM", 4, 10, Floor, TitleSize, TitleColor);
        Label(s, "Get in range -", 4, 8.7f, Floor, HintSize, HintColor);
        Label(s, "you fire on your own", 4, 7.7f, Floor, HintSize, HintColor);
        Label(s, "Targets respawn", 16, 0.8f, Floor, HintSize * 0.8f, HintColor);
        return s;
    }

    // Hero skill (HeroSkill input) on a tight pack of dummies - most hero skills are area effects
    // (Vortex, Cluster Bomb, Overdrive, Totem, Sentry), so a group shows what the skill does. Text
    // stays generic: the zone doesn't know which hero is playing.
    private static GameObject BuildSkill(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "9_Skill", ChunkType.Enemy, ref x, 18);
        Box(s, "Floor", 0, 0, 18, Depth, Floor, false);
        Respawn(s, 2, 6, Floor);

        Dummy(s, 11, 4.6f);
        Dummy(s, 12.6f, 4.6f);
        Dummy(s, 11.8f, 6.1f);
        Dummy(s, 11, 7.6f);
        Dummy(s, 12.6f, 7.6f);

        Label(s, "SKILL", 4, 9.6f, Floor, TitleSize, TitleColor);
        Label(s, "E  /  X", 4, 8.3f, Floor, HintSize, HintColor);
        Label(s, "Use it on a group", 4, 4.4f, Floor, HintSize, HintColor);
        return s;
    }

    // End of the course: the Finish POI (TrainingFinishPoi prefab - TraversalActivator look as a
    // placeholder). Its Interactable{Kind = TrainingFinish} goes through the normal Base-Skill
    // interaction, so the last lesson is "how to interact"; the press returns to the menu.
    private static GameObject BuildFinish(Transform zone, ref float x)
    {
        GameObject s = Station(zone, "10_Finish", ChunkType.Enemy, ref x, 12);
        Box(s, "Floor", 0, 0, 12, Depth, Floor, false);
        Respawn(s, 2, 6, Floor);

        var poiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FinishPoiPath);
        var poi = (GameObject)PrefabUtility.InstantiatePrefab(poiPrefab, s.transform);
        poi.transform.localPosition = new Vector3(8, Floor + 0.5f, 7);

        Label(s, "INTERACT", 4, 4.2f, Floor, TitleSize, TitleColor);
        Label(s, "E  /  X  next to the sign", 4, 3.0f, Floor, HintSize, HintColor);
        return s;
    }

    // ---------------------------------------------------------------- building blocks

    // Station roots (the Chunk map entities) of the current Build, for the collider/respawn bake pass.
    private static readonly List<GameObject> _stationRoots = new List<GameObject>();

    // Returns the station's CONTENT object: every station except the first starts with a flat
    // BreathingRoom-wide strip of plain floor (part of the same chunk) and its content is offset past
    // it - a breather between lessons, and room for a station's text to pop in ahead of the player
    // instead of under them. Builders lay out in content-local coords and pass `width` = content only.
    private static GameObject Station(Transform zone, string name, ChunkType type, ref float x, int width)
    {
        int lead = _stationRoots.Count == 0 ? 0 : BreathingRoom;
        width += lead;

        var go = new GameObject(name);
        go.layer = _floorTemplate.layer;
        go.transform.SetParent(zone, false);
        go.transform.localPosition = new Vector3(x, 0f, 0f);
        x += width;
        _stationRoots.Add(go);

        // Same Quantum entity setup as the template chunk (PhysicsCollider layer/flags, transform mode),
        // then this station's own Chunk data on top.
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplateChunkPath);
        var prototype = go.AddComponent<QuantumEntityPrototype>();
        EditorUtility.CopySerialized(template.GetComponent<QuantumEntityPrototype>(), prototype);

        var chunk = go.AddComponent<QPrototypeChunk>();
        chunk.Prototype = new Quantum.Prototypes.Unity.ChunkPrototype
        {
            Type = type,
            ChunkSizeWidth = width,
            ChunkSizeDepth = Depth,
            AllowedConnectionSides = ChunkConnectionSide.Left | ChunkConnectionSide.Right,
        };

        go.AddComponent<ChunkCompoundColliderBuilder>();
        go.AddComponent<ChunkRespawnPointBaker>();

        if (lead > 0)
            Box(go, "BreathingRoom", 0, 0, lead, Depth, Floor, false);

        var content = new GameObject("Content");
        content.layer = go.layer;
        content.transform.SetParent(go.transform, false);
        content.transform.localPosition = new Vector3(lead, 0f, 0f);
        return content;
    }

    // Min-corner box in station-local coords: x/z/width/depth on the floor plan, top = walkable height.
    private static void Box(GameObject station, string name, float x, float z, float width, float depth, float top, bool raised)
    {
        GameObject cube = Object.Instantiate(raised ? _blockTemplate : _floorTemplate, station.transform, false);
        cube.name = name;
        // TraversalChunk's floor cube template is authored inactive - every cube here must be live.
        cube.SetActive(true);
        cube.transform.localPosition = new Vector3(x, Bottom, z);
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = new Vector3(width, top - Bottom, depth);
    }

    private static void Respawn(GameObject station, float x, float z, float top)
    {
        var marker = new GameObject("RespawnPoint");
        marker.transform.SetParent(station.transform, false);
        marker.transform.localPosition = new Vector3(x, top + 1f, z);
        marker.AddComponent<ChunkRespawnPointMarker>();
    }

    // Min-corner strip on the floor: a SpikeTrap map entity (Step trigger, TeachingHit - see SpikeTrap.qtn)
    // whose SpikeTrapView builds the plates + spikes at runtime, plus yellow/black hazard-stripe borders
    // on both long sides and a caution sign standing at each end.
    private static void Hazard(GameObject station, float x, float z, float width, float depth)
    {
        var go = new GameObject("SpikeTrap");
        go.transform.SetParent(station.transform, false);
        go.transform.localPosition = new Vector3(x + width * 0.5f, Floor, z + depth * 0.5f);

        go.AddComponent<QuantumEntityPrototype>().TransformMode = QuantumEntityPrototypeTransformMode.Transform3D;
        var trap = go.AddComponent<QPrototypeSpikeTrap>();
        trap.Prototype = new Quantum.Prototypes.SpikeTrapPrototype
        {
            Trigger = SpikeTrapTrigger.Step,
            TargetMask = DamageTargetMask.Players,
            HalfExtents = new FPVector2(FP.FromFloat_UNSAFE(width * 0.5f), FP.FromFloat_UNSAFE(depth * 0.5f)),
            Damage = FP.FromFloat_UNSAFE(HazardDamage),
            // Spikes fire the instant they're stepped on - the teaching hit can't be outrun or dashed past.
            WarningTime = FP._0,
            ExtendedTime = FP.FromFloat_UNSAFE(0.8f),
            HitInterval = FP.FromFloat_UNSAFE(0.2f),
            CooldownTime = FP.FromFloat_UNSAFE(1.2f),
            TeachingHit = true,
            // Throw the accessory forward (screen right), past the trap toward "PICK IT BACK UP".
            AccessoryDropDirection = new FPVector2(FP._1, FP._0),
            AccessoryDropSpread = FP.FromFloat_UNSAFE(20f),
        };
        go.AddComponent<QuantumEntityView>();

        SpriteRenderer telegraph = FlatSprite(go.transform, "WarningTelegraph", TelegraphFillSprite, width, depth, 0.2f, 1);
        telegraph.enabled = false;
        var view = go.AddComponent<SpikeTrapView>();
        var so = new SerializedObject(view);
        so.FindProperty("warningTelegraph").objectReferenceValue = telegraph;
        so.FindProperty("fallbackMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(SpikeFallbackMaterialPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Hazard-stripe borders just outside both long edges.
        foreach (float side in new[] { -1f, 1f })
        {
            GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Quad);
            stripe.name = side < 0 ? "StripeLeft" : "StripeRight";
            Object.DestroyImmediate(stripe.GetComponent<Collider>());
            stripe.transform.SetParent(go.transform, false);
            stripe.transform.localPosition = new Vector3(side * (width * 0.5f + StripeWidth * 0.5f), 0.025f, 0f);
            stripe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            stripe.transform.localScale = new Vector3(StripeWidth, depth, 1f);
            MeshRenderer stripeRenderer = stripe.GetComponent<MeshRenderer>();
            stripeRenderer.sharedMaterial = LoadOrCreateStripeMaterial(depth);
            stripeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // One warning sign (view only) on the approach side, before the trap (user layout): the Rift POI sign
        // board the Finish POI uses, with the caution symbol on its face and the POI's warm glow on top.
        var sign = new GameObject("WarningSignNear");
        sign.transform.SetParent(go.transform, false);
        sign.transform.localPosition = new Vector3(-2.3f, 0f, -5.43f);
        sign.transform.localScale = Vector3.one * 0.5f;
        sign.AddComponent<Billboard>();

        var board = sign.AddComponent<SpriteRenderer>();
        board.sprite = LoadSubSprite(PoiSignSheetPath, PoiSignSpriteName);

        // The POI's warm warning glow (TraversalActivator's "Available" particles), on the sign's lamp.
        var glowSource = AssetDatabase.LoadAssetAtPath<GameObject>(TraversalActivatorPath).transform.Find("Available");
        if (glowSource != null)
        {
            GameObject glow = Object.Instantiate(glowSource.gameObject, sign.transform, false);
            glow.name = "WarningGlow";
            glow.transform.localPosition = new Vector3(0f, 2.723f, -0.01f);
            glow.transform.localRotation = Quaternion.identity;
            glow.transform.localScale = Vector3.one * 2f;
        }

        var icon = new GameObject("CautionIcon");
        icon.transform.SetParent(sign.transform, false);
        var iconSprite = icon.AddComponent<SpriteRenderer>();
        iconSprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CautionSpritePath);
        iconSprite.sortingOrder = board.sortingOrder + 1;
        // The icon is white - tinted alert red so it reads on the board's white face.
        iconSprite.color = new Color32(0xD9, 0x38, 0x1E, 0xFF);

        // On the board face (y set by eye), ~55% of the board's width.
        if (board.sprite != null && iconSprite.sprite != null)
        {
            Bounds face = board.sprite.bounds;
            icon.transform.localPosition = new Vector3(face.center.x, 1.67f, -0.01f);
            icon.transform.localScale = Vector3.one * (face.size.x * 0.55f / iconSprite.sprite.bounds.size.x);
        }
    }

    private const string TraversalActivatorPath = "Assets/_QuantumUser/Entities/LevelChunk/TraversalActivator.prefab";
    private const string PoiSignSheetPath = "Assets/_Project/Art/Sprites/Environment/RiftRaiders_POI.png";
    private const string PoiSignSpriteName = "RR_TrainingFinishSign";

    private static Sprite LoadSubSprite(string sheetPath, string spriteName)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
        {
            if (asset is Sprite sprite && sprite.name == spriteName)
                return sprite;
        }

        LogHelper.Warn("TrainingZone", $"Sprite {spriteName} not found in {sheetPath}.");
        return null;
    }

    private const string SpikeFallbackMaterialPath = "Assets/Test/Environment/Tileset/GrassOutpostV2/GrassOutpostV2_Toon.mat";
    private const string TelegraphFillSprite = "Assets/_Project/Art/Textures/SquareTelegraphFull.png";
    private const string CautionSpritePath = "Assets/3rd-party/Layer Lab/GUI Pro-SuperCasual/ResourcesData/Sprites/Components/Icon_PictoIcons/128/PictoIcon_Caution.Png";
    private const string StripeTexturePath = "Assets/_Project/Art/Textures/TrainingHazardStripes.png";
    private const string StripeMaterialPath = "Assets/_Project/Art/Material/TrainingHazardStripes.mat";
    private const float StripeWidth = 0.4f;

    // Same red the enemy SquareTelegraph prefab uses.
    private static readonly Color TelegraphRed = new Color(1f, 0f, 0f, 0.4f);

    private static SpriteRenderer FlatSprite(Transform parent, string name, string spritePath, float width, float depth, float y, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        renderer.color = TelegraphRed;
        renderer.sortingOrder = order;

        Bounds bounds = renderer.sprite != null ? renderer.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
        go.transform.localScale = new Vector3(width / bounds.size.x, depth / bounds.size.y, 1f);

        // Centre the sprite on the strip whatever its pivot (the telegraph fill's pivot is its left edge).
        // Sprite-local X/Y lie along parent X/Z once laid flat.
        go.transform.localPosition = new Vector3(-bounds.center.x * go.transform.localScale.x, y, -bounds.center.y * go.transform.localScale.y);
        return renderer;
    }

    // Diagonal yellow/black stripes, generated once (64px tile, one stripe pair per tile).
    private static Material LoadOrCreateStripeMaterial(float length)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(StripeMaterialPath);

        if (material == null)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(StripeTexturePath) == null)
            {
                var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                var yellow = new Color32(0xF2, 0xC0, 0x14, 0xFF);
                var black = new Color32(0x1A, 0x1A, 0x1A, 0xFF);

                for (int py = 0; py < 64; py++)
                    for (int px = 0; px < 64; px++)
                        texture.SetPixel(px, py, ((px + py) / 32) % 2 == 0 ? yellow : black);

                System.IO.File.WriteAllBytes(StripeTexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(StripeTexturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(StripeTexturePath);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StripeTexturePath));
            AssetDatabase.CreateAsset(material, StripeMaterialPath);
        }

        // Square tiles: one tile per StripeWidth along the strip's length.
        material.SetTextureScale("_BaseMap", new Vector2(1f, length / StripeWidth));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Dummy(GameObject station, float x, float z)
    {
        var go = new GameObject("TargetDummySpawner");
        go.transform.SetParent(station.transform, false);
        go.transform.localPosition = new Vector3(x, Floor, z);

        // TrainingDummySpawnerSystem filters on Transform3D - a fresh prototype defaults to Transform2D.
        go.AddComponent<QuantumEntityPrototype>().TransformMode = QuantumEntityPrototypeTransformMode.Transform3D;
        var spawner = go.AddComponent<QPrototypeTrainingDummySpawner>();
        spawner.Prototype = new Quantum.Prototypes.Unity.TrainingDummySpawnerPrototype
        {
            Prototype = FindDirectorEnemyPrototype(),
            EnemyData = AssetDatabase.LoadAssetAtPath<EnemyDataAsset>(DummyEnemyDataPath),
            RespawnDelay = FP.FromFloat_UNSAFE(DummyRespawnDelay),
        };
    }

    // Text lying flat on the ground, readable from the gameplay camera (looks +Z, tilted down).
    // onGround false = floating over water (still flat) for stations whose floor is all gaps.
    private static void Label(GameObject station, string text, float x, float z, float top, float size, Color color, bool onGround = true)
    {
        var go = new GameObject($"Text_{text}");
        go.transform.SetParent(station.transform, false);
        go.transform.localPosition = new Vector3(x, top + (onGround ? 0.04f : 0f), z);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(20f, 2f);

        // Ground decals - no shadow casting, drawn over the floor.
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ---------------------------------------------------------------- helpers

    private static bool LoadTemplates()
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplateChunkPath);

        if (template == null)
        {
            LogHelper.Error("TrainingZone", $"Template chunk not found at {TemplateChunkPath}.");
            return false;
        }

        // Floor = a plain chunk floor cube; Block = a raised cube (also carries HasBuildingShadow).
        _floorTemplate = template.transform.Find("VisualCube")?.gameObject;
        _blockTemplate = template.transform.Find("VisualCube (13)")?.gameObject;
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        if (_floorTemplate == null || _blockTemplate == null || _font == null)
        {
            LogHelper.Error("TrainingZone", "Missing template cube(s) on TraversalChunk or the label font.");
            return false;
        }

        return true;
    }

    private static AssetRef<EntityPrototype> FindDirectorEnemyPrototype()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:DirectorConfig"))
        {
            var config = AssetDatabase.LoadAssetAtPath<DirectorConfig>(AssetDatabase.GUIDToAssetPath(guid));

            if (config != null && config.EnemyPrototype.IsValid)
                return config.EnemyPrototype;
        }

        LogHelper.Error("TrainingZone", "No DirectorConfig with an EnemyPrototype found - dummies won't spawn.");
        return default;
    }

    private static QuantumMapData FindMapData(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var mapData = root.GetComponentInChildren<QuantumMapData>(true);

            if (mapData != null)
                return mapData;
        }

        return null;
    }
}
