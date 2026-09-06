// DepthWizardSceneSetup.cs
//
// One-click Phase 1 scene wiring for Person 2 (rendering shell) and
// Person 3 (camera + interaction shell) work. Every step here is written
// against the currently open scene through public Editor APIs, so the same
// menu items apply unchanged to Team A's next terrain scenes - nothing is
// hardcoded to SampleScene or to patch_43.
//
// Run "DepthWizard > Setup > Run Phase 1 Scene Setup" to do all three steps
// below at once, or run them individually. Lighting is safe to re-run
// (it just re-applies the same values); the camera rig / manifest loader
// steps are no-ops if that piece is already wired up in the scene.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class DepthWizardSceneSetup
{
    // Low raking angle: high enough to clear the horizon, low enough that
    // ridgelines throw long shadows instead of the flat, shadowless look a
    // near-overhead sun gives a heightmap.
    static readonly Vector3 SunEulerAngles = new Vector3(25f, -35f, 0f);

    // Horizon color is shared between the gradient sky material and
    // RenderSettings.fogColor on purpose - see ConfigureSkyAndFog - so
    // fogged terrain fades into the sky instead of showing a seam.
    static readonly Color SkyColor = new Color(0.35f, 0.55f, 0.85f);
    static readonly Color HorizonColor = new Color(0.75f, 0.8f, 0.85f);
    static readonly Color GroundColor = new Color(0.3f, 0.28f, 0.25f);

    [MenuItem("DepthWizard/Setup/Run Phase 1 Scene Setup")]
    public static void RunPhase1Setup()
    {
        ConfigureLighting();
        AddCameraRig();
        AddManifestLoader();
    }

    [MenuItem("DepthWizard/Setup/Run Phase 2 Scene Setup")]
    public static void RunPhase2Setup()
    {
        ConfigureSkyAndFog();
        AddLakeWaterPlane();
        AddElevationReadoutUI();
        Debug.Log("DepthWizard: Phase 2 auto-steps done. Texture draping needs a manual texture reference - " +
                   "run DepthWizard > Setup > Drape Terrain Texture... separately once the source .tif is imported.");
    }

    [MenuItem("DepthWizard/Setup/Configure Lighting")]
    public static void ConfigureLighting()
    {
        Light sun = FindOrCreateDirectionalLight();

        Undo.RecordObject(sun.transform, "Configure DepthWizard Lighting");
        Undo.RecordObject(sun, "Configure DepthWizard Lighting");
        sun.transform.rotation = Quaternion.Euler(SunEulerAngles);
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.88f); // slightly warm, reads as sunlight rather than a studio light
        sun.intensity = 1.3f;
        sun.shadows = LightShadows.Soft;

        // Ambient/environment fill light. Trilight is used instead of
        // Skybox so scene lighting looks correct even before a scene has a
        // custom skybox assigned (Phase 2 adds a gradient sky) - it doesn't
        // depend on any other asset being set up first.
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.75f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.25f, 0.2f);
        RenderSettings.ambientIntensity = 1f;

        MarkSceneDirty();
        Debug.Log($"DepthWizard: configured '{sun.name}' as a low-angle sun ({SunEulerAngles}) plus trilight ambient. " +
                   "RenderSettings changes aren't Undo-tracked - save the scene (Ctrl+S) to keep them.");
    }

    [MenuItem("DepthWizard/Setup/Add Camera Rig")]
    public static void AddCameraRig()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("DepthWizard: no Main Camera found in the open scene - add one first (GameObject > Camera, tag it MainCamera).");
            return;
        }

        if (mainCamera.GetComponent<OrbitFlyCamera>() != null)
        {
            Debug.Log($"DepthWizard: '{mainCamera.name}' already has an OrbitFlyCamera - nothing to do.");
            return;
        }

        Undo.AddComponent<OrbitFlyCamera>(mainCamera.gameObject);
        MarkSceneDirty();
        Debug.Log($"DepthWizard: added OrbitFlyCamera to '{mainCamera.name}'. Right-drag to orbit, Tab to toggle WASD fly mode.");
    }

    [MenuItem("DepthWizard/Setup/Add Manifest Loader")]
    public static void AddManifestLoader()
    {
        ManifestSceneLoader existing = Object.FindFirstObjectByType<ManifestSceneLoader>();
        if (existing != null)
        {
            Debug.Log($"DepthWizard: a ManifestSceneLoader already exists on '{existing.name}' - nothing to do.");
            return;
        }

        GameObject go = new GameObject("Scene Manifest Loader");
        Undo.RegisterCreatedObjectUndo(go, "Add Manifest Loader");
        Undo.AddComponent<ManifestSceneLoader>(go);

        MarkSceneDirty();
        Debug.Log("DepthWizard: created 'Scene Manifest Loader'. Set its Scene Id field in the Inspector to match the " +
                   "patch you want it to log (defaults to \"patch_43\"), then press Play and check the Console.");
    }

    [MenuItem("DepthWizard/Setup/Drape Terrain Texture...")]
    public static void OpenDrapeTextureWindow()
    {
        DepthWizardTextureDrapeWindow.Open();
    }

    /// <summary>
    /// Drapes a diffuse texture over a terrain as its single Terrain Layer,
    /// tiled to exactly match that terrain's world size so it lines up 1:1
    /// with the heightmap's footprint instead of repeating. Called from
    /// DepthWizardTextureDrapeWindow, which supplies the Texture2D a plain
    /// menu item can't (we have no path to load it from - each scene's
    /// source .tif lives wherever it was imported).
    /// </summary>
    public static void DrapeTexture(Terrain terrain, Texture2D texture, bool flipVertically = true)
    {
        if (terrain == null || texture == null)
        {
            Debug.LogError("DepthWizard: DrapeTexture needs both a terrain and a texture.");
            return;
        }

        // TerrainLayer is a plain UnityEngine.Object (like Material/Mesh/Texture2D),
        // not a ScriptableObject, so it's constructed directly rather than via CreateInstance.
        TerrainLayer layer = new TerrainLayer();
        layer.diffuseTexture = texture;

        Vector3 size = terrain.terrainData.size;
        // One tile spans the whole terrain so the texture maps 1:1 to the
        // heightmap's footprint instead of repeating. The V-axis flip
        // corrects a mismatch between how DepthWizardTerrainBuilder loads
        // heightmap PNGs (row 0 -> terrain Z=0, read straight off disk, no
        // importer involved) and how Unity's normal texture importer
        // orients images (V=0 -> the image's bottom row) - without it the
        // drape comes out mirrored top-to-bottom relative to the relief.
        layer.tileSize = new Vector2(size.x, flipVertically ? -size.z : size.z);
        layer.tileOffset = flipVertically ? new Vector2(0f, size.z) : Vector2.zero;

        EnsureFolder("Assets/Terrain", "Generated");
        string path = AssetDatabase.GenerateUniqueAssetPath($"Assets/Terrain/Generated/{terrain.name}_TerrainLayer.asset");
        AssetDatabase.CreateAsset(layer, path);

        terrain.terrainData.terrainLayers = new[] { layer };

        MarkSceneDirty();
        Debug.Log($"DepthWizard: draped '{texture.name}' onto '{terrain.name}' as {path} (flipVertically={flipVertically}). " +
                   "If the result looks upside-down against the terrain's ridgelines/lake shape, reopen the drape window and toggle Flip Vertically.");
    }

    [MenuItem("DepthWizard/Setup/Configure Sky And Fog")]
    public static void ConfigureSkyAndFog()
    {
        Material skyMaterial = FindOrCreateSkyMaterial();
        if (skyMaterial != null)
        {
            RenderSettings.skybox = skyMaterial;
        }

        // Now that a real sky exists, let ambient light read its colors
        // (blue-ish from above, ground-ish from below) instead of Phase 1's
        // fixed Trilight fill.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();

        Terrain terrain = Terrain.activeTerrain;
        float diagonal = terrain != null
            ? new Vector2(terrain.terrainData.size.x, terrain.terrainData.size.z).magnitude
            : 2000f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = HorizonColor;
        RenderSettings.fogStartDistance = diagonal * 0.3f;
        RenderSettings.fogEndDistance = diagonal * 1.1f;

        MarkSceneDirty();
        Debug.Log($"DepthWizard: assigned the gradient sky, switched ambient to Skybox mode, and enabled linear fog " +
                   $"from {RenderSettings.fogStartDistance:F0}m to {RenderSettings.fogEndDistance:F0}m " +
                   $"(sized off {(terrain != null ? terrain.name : "a 2000m default - no active terrain found")}). " +
                   "RenderSettings changes aren't Undo-tracked - save the scene (Ctrl+S) to keep them.");
    }

    [MenuItem("DepthWizard/Setup/Add Lake Water Plane")]
    public static void AddLakeWaterPlane()
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogWarning("DepthWizard: no active terrain found - can't size/position a water plane without one.");
            return;
        }

        if (GameObject.Find("Lake Water Plane") != null)
        {
            Debug.Log("DepthWizard: 'Lake Water Plane' already exists - nothing to do.");
            return;
        }

        Vector3 size = terrain.terrainData.size;
        Vector3 center = terrain.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);

        // Sit a hair above the true floor elevation (terrain.transform.position.y,
        // which DepthWizardTerrainBuilder sets to elevation_range_m.min) -
        // the pinned lake-bed pixels sit at exactly that height, so a
        // perfectly coplanar plane would z-fight with the terrain there.
        const float zFightGuard = 0.05f;
        float waterY = terrain.transform.position.y + zFightGuard;

        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Lake Water Plane";
        Object.DestroyImmediate(water.GetComponent<Collider>()); // keep elevation raycasts hitting the real terrain, not this sheet
        Undo.RegisterCreatedObjectUndo(water, "Add Lake Water Plane");

        water.transform.position = new Vector3(center.x, waterY, center.z);
        // Unity's Plane primitive is 10x10 units at scale 1; overscale by 2%
        // so the edge hides just past the terrain border instead of leaving a seam.
        water.transform.localScale = new Vector3(size.x / 10f * 1.02f, 1f, size.z / 10f * 1.02f);

        Material waterMaterial = FindOrCreateWaterMaterial();
        if (waterMaterial != null)
        {
            water.GetComponent<Renderer>().sharedMaterial = waterMaterial;
        }

        MarkSceneDirty();
        Debug.Log($"DepthWizard: added 'Lake Water Plane' at Y={waterY:F2} under '{terrain.name}' " +
                   "(floor elevation + a small z-fight guard). Its Collider was removed on purpose - see ElevationDisplay's header comment.");
    }

    [MenuItem("DepthWizard/Setup/Add Elevation Readout UI")]
    public static void AddElevationReadoutUI()
    {
        if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0)
        {
            Debug.LogWarning("DepthWizard: TextMeshPro essential resources aren't imported yet. " +
                               "Run Window > TextMeshPro > Import TMP Essential Resources (click Import All), then re-run this command.");
            return;
        }

        ElevationDisplay existing = Object.FindFirstObjectByType<ElevationDisplay>();
        if (existing != null)
        {
            Debug.Log($"DepthWizard: an ElevationDisplay already exists on '{existing.name}' - nothing to do.");
            return;
        }

        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("DepthWizard UI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Add Elevation Readout UI");
            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        GameObject textGO = new GameObject("Elevation Readout", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(textGO, "Add Elevation Readout UI");
        textGO.transform.SetParent(canvas.transform, false);

        RectTransform rect = textGO.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(24f, 24f);
        rect.sizeDelta = new Vector2(420f, 60f);

        TextMeshProUGUI text = textGO.AddComponent<TextMeshProUGUI>();
        text.fontSize = 28f;
        text.color = Color.white;
        text.text = "Elevation: -- m";

        // ElevationDisplay lives on the same GameObject as the text and
        // auto-wires to it in Awake() - see its header comment.
        Undo.AddComponent<ElevationDisplay>(textGO);

        MarkSceneDirty();
        Debug.Log("DepthWizard: added an on-screen elevation readout. Press Play and hover the terrain to see live elevation. " +
                   "RenderSettings changes aren't part of this step, but other Phase 2 steps' are - save the scene (Ctrl+S).");
    }

    static Material FindOrCreateSkyMaterial()
    {
        const string path = "Assets/Rendering/Generated/DepthWizard_GradientSky.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("DepthWizard/GradientSky");
        if (shader == null)
        {
            Debug.LogError("DepthWizard: shader 'DepthWizard/GradientSky' not found - is Assets/Shaders/DepthWizardGradientSky.shader present and compiling?");
            return null;
        }

        Material mat = new Material(shader);
        mat.SetColor("_SkyColor", SkyColor);
        mat.SetColor("_HorizonColor", HorizonColor);
        mat.SetColor("_GroundColor", GroundColor);

        EnsureFolder("Assets/Rendering", "Generated");
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material FindOrCreateWaterMaterial()
    {
        const string path = "Assets/Rendering/Generated/DepthWizard_Water.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("DepthWizard/Water");
        if (shader == null)
        {
            Debug.LogError("DepthWizard: shader 'DepthWizard/Water' not found - is Assets/Shaders/DepthWizardWater.shader present and compiling?");
            return null;
        }

        Material mat = new Material(shader);
        mat.SetColor("_Color", new Color(0.15f, 0.35f, 0.5f, 0.75f));

        EnsureFolder("Assets/Rendering", "Generated");
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string parentPath, string folderName)
    {
        if (!AssetDatabase.IsValidFolder(parentPath))
        {
            string[] parts = parentPath.Split('/');
            AssetDatabase.CreateFolder(parts[0], parts[1]);
        }
        if (!AssetDatabase.IsValidFolder($"{parentPath}/{folderName}"))
        {
            AssetDatabase.CreateFolder(parentPath, folderName);
        }
    }

    static Light FindOrCreateDirectionalLight()
    {
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional)
            {
                return light;
            }
        }

        GameObject go = new GameObject("Directional Light");
        Undo.RegisterCreatedObjectUndo(go, "Create Directional Light");
        Light created = go.AddComponent<Light>();
        created.type = LightType.Directional;
        return created;
    }

    static void MarkSceneDirty()
    {
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
