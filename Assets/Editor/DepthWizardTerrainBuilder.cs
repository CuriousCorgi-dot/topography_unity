// DepthWizardTerrainBuilder.cs
//
// Builds a Unity Terrain directly from a 16-bit grayscale heightmap PNG,
// using only public Terrain/TerrainData APIs - no dependency on the Terrain
// Toolbox window itself, so it runs from one menu click with no dialogs.
//
// Why this exists instead of using Terrain Toolbox's GUI: the numbers below
// (world size, elevation range) come straight from Team A's manifest.json,
// so there is no scaling math to get wrong by hand.
//
// IMPORTANT: run "DepthWizard > Build Pyramid Test Terrain" FIRST and check
// the Console log it prints. A correct run reports the corner at ~0m and the
// center at ~1000m. If those numbers are off, do not trust the real-data run.
//
// NOTE ON HEIGHTMAP LOADING: this deliberately does NOT go through Unity's
// AssetImporter/TextureImporter/AssetDatabase pipeline at all. That route
// (import as a Texture2D asset, flip isReadable, force an R16 platform
// override, SaveAndReimport, then GetPixels) turned out to be fragile in
// practice - a texture that failed to import cleanly once can get stuck in
// a state where GetPixels keeps throwing "not readable" even after
// isReadable is set, and even a "successful" reimport risks Unity quietly
// re-quantizing a 16-bit PNG down to 8 bits per channel, which would throw
// away most of the elevation detail without any error at all.
//
// Instead this reads the PNG file's bytes straight off disk and decodes the
// 16-bit grayscale pixel data itself. That logic (and the plain TerrainData/
// GameObject building below it) now lives in TerrainBuildUtility
// (Assets/Scripts/Terrain), shared with ManifestSceneLoader's runtime
// terrain-swap path - see that file's header for why it had to move out of
// this Editor-only script.
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Callbacks;

public static class DepthWizardTerrainBuilder
{
    // Auto-run the pyramid test the moment this script finishes compiling -
    // no menu click needed. Guarded so it only builds once; delete the
    // PyramidTest_Terrain GameObject from the scene if you want it to redo.
    //
    // Currently disabled (see commit history) - it collided with the Phase 1/2
    // one-click scene setup workflow, so builds are menu-triggered only now.
    // [DidReloadScripts]
    static void AutoRunOnce()
    {
        bool pyramidExists = GameObject.Find("PyramidTest_Terrain") != null;
        if (!pyramidExists)
        {
            string fullPath = System.IO.Path.Combine(Application.dataPath, "..", "Assets/Terrain/PyramidTest/pyramid_heightmap.png");
            if (System.IO.File.Exists(fullPath))
            {
                Debug.Log("DepthWizard: auto-running pyramid test now that the script compiled...");
                BuildPyramidTest();
                pyramidExists = true;
            }
        }

        // Also auto-run the real-data build if that heightmap is present too -
        // MenuItem clicks have proven unreliable to drive by remote GUI
        // automation, so do both from the one recompile.
        bool patch43Exists = GameObject.Find("Patch43_Terrain") != null;
        if (!patch43Exists)
        {
            string patch43Path = System.IO.Path.Combine(Application.dataPath, "..", "Assets/Terrain/Heightmaps/patch_43_heightmap.png");
            if (System.IO.File.Exists(patch43Path))
            {
                Debug.Log("DepthWizard: auto-running patch_43 real-data build too...");
                BuildPatch43();
                patch43Exists = true;
            }
        }

        // IMPORTANT: creating GameObjects from an editor script like this
        // does NOT mark the scene dirty the way a manual edit would - so
        // File > Save (and Ctrl+S) can silently no-op even with the new
        // terrain sitting right there in the Hierarchy, leaving it out of
        // the saved .unity file entirely. Force the save explicitly instead
        // of trusting Unity's dirty-tracking.
        if (pyramidExists || patch43Exists)
        {
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"DepthWizard: force-saved scene '{scene.name}' to disk (script-created GameObjects don't auto-dirty the scene).");
        }
    }

    [MenuItem("DepthWizard/Build Pyramid Test Terrain")]
    public static void BuildPyramidTest()
    {
        BuildTerrainFromHeightmap(
            heightmapAssetPath: "Assets/Terrain/PyramidTest/pyramid_heightmap.png",
            worldWidth: 1000f,
            worldLength: 1000f,
            elevationMin: 0f,
            elevationMax: 1000f,
            terrainName: "PyramidTest_Terrain",
            isKnownShapeTest: true
        );
    }

    [MenuItem("DepthWizard/Build Patch 43 Terrain (real data)")]
    public static void BuildPatch43()
    {
        BuildTerrainFromHeightmap(
            heightmapAssetPath: "Assets/Terrain/Heightmaps/patch_43_heightmap.png",
            worldWidth: 5120.43f,
            worldLength: 5133.73f,
            elevationMin: 379.0f,
            elevationMax: 718.54f,
            terrainName: "Patch43_Terrain",
            isKnownShapeTest: false
        );
    }

    static void BuildTerrainFromHeightmap(
        string heightmapAssetPath, float worldWidth, float worldLength,
        float elevationMin, float elevationMax, string terrainName, bool isKnownShapeTest)
    {
        string fullPath = System.IO.Path.Combine(Application.dataPath, "..", heightmapAssetPath);
        if (!System.IO.File.Exists(fullPath))
        {
            Debug.LogError($"DepthWizard: no file at '{fullPath}'. " +
                            "Check the path matches where the file actually is in Assets/Terrain.");
            return;
        }

        ushort[,] raw16;
        int srcW, srcH;
        try
        {
            raw16 = TerrainBuildUtility.LoadGrayscale16Png(fullPath, out srcW, out srcH);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"DepthWizard: failed to decode 16-bit grayscale PNG at '{heightmapAssetPath}': {e.Message}");
            return;
        }

        int resolution = TerrainBuildUtility.PickHeightmapResolution(Mathf.Max(srcW, srcH));
        float[,] heights = TerrainBuildUtility.BuildHeightsArray(raw16, srcW, srcH, resolution);

        GameObject existing = GameObject.Find(terrainName);
        if (existing != null) Object.DestroyImmediate(existing);

        GameObject terrainGO = TerrainBuildUtility.CreateTerrainGameObject(
            heights, resolution, worldWidth, worldLength, elevationMin, elevationMax, terrainName,
            out TerrainData terrainData);

        // Editor-only: persist the TerrainData as a visible project asset so
        // it survives between Editor sessions without rebuilding. The
        // runtime swap path (ManifestSceneLoader) deliberately skips this -
        // its TerrainData only needs to live as long as the Play session.
        const string dataDir = "Assets/Terrain/Generated";
        if (!AssetDatabase.IsValidFolder(dataDir))
            AssetDatabase.CreateFolder("Assets/Terrain", "Generated");
        string dataAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{dataDir}/{terrainName}Data.asset");
        AssetDatabase.CreateAsset(terrainData, dataAssetPath);
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = terrainGO;
        EditorGUIUtility.PingObject(terrainGO);

        Debug.Log($"DepthWizard: built '{terrainName}' from {srcW}x{srcH} heightmap -> " +
                  $"terrain resolution {resolution}. Size {worldWidth}m x {elevationMax - elevationMin}m x {worldLength}m, " +
                  $"elevation {elevationMin}m to {elevationMax}m.");

        if (isKnownShapeTest)
        {
            int last = resolution - 1;
            int center = resolution / 2;
            float cornerElevation = elevationMin + terrainData.GetHeight(0, 0);
            float farCornerElevation = elevationMin + terrainData.GetHeight(last, last);
            float centerElevation = elevationMin + terrainData.GetHeight(center, center);

            Debug.Log("DepthWizard PYRAMID CHECK - expect corner ~0m, center ~1000m:\n" +
                      $"  corner (0,0):        {cornerElevation:F1} m\n" +
                      $"  corner ({last},{last}): {farCornerElevation:F1} m\n" +
                      $"  center ({center},{center}): {centerElevation:F1} m");

            bool ok = Mathf.Abs(cornerElevation) < 5f
                      && Mathf.Abs(farCornerElevation) < 5f
                      && Mathf.Abs(centerElevation - 1000f) < 5f;

            if (ok)
                Debug.Log("DepthWizard: PASS - pyramid shape and scale are correct. Safe to run the real-data build.");
            else
                Debug.LogError("DepthWizard: FAIL - numbers above don't match the known pyramid. " +
                                "Do not trust the real-data terrain until this is fixed.");
        }
    }
}
