// ManifestSceneLoader.cs
//
// Given a scene/patch id, finds that patch's manifest.json under
// Assets/<searchRoot>, parses+validates it, then builds and swaps in the
// matching terrain at runtime - no Editor re-import step involved. This is
// the Phase 3 roadmap item ("Runtime loader in C#... so scenes swap while
// the app runs"). Nothing here is hardcoded to patch_43: point sceneId at
// any id and it finds that manifest by the same two lookup rules Team A's
// files already follow, then the matching heightmap by the sibling-file
// convention (see FindHeightmapPath below).
//
// IMPORTANT GOTCHA: manifest.heightmap (e.g. "target/heightmap.png") is
// Team A's ORIGINAL raw delivery path, not a path that exists anywhere in
// this Unity project - do not use it to locate the file. Every heightmap
// actually in this repo instead sits right next to its manifest, named
// "<id>_heightmap.png" (see Assets/Terrain/Heightmaps and
// Assets/Terrain/PyramidTest), so that's the convention this file follows.
//
// Terrain building itself (PNG decode, heights array, GameObject/TerrainData
// setup) is shared with the Editor-only DepthWizardTerrainBuilder via
// TerrainBuildUtility (Assets/Scripts/Terrain) - see that file's header for
// why the logic lives there instead of duplicated in both places.
//
// EDITOR/DEV-ONLY FILE LOOKUP: FindManifestPath/FindHeightmapPath walk
// Application.dataPath directly, which only works when the Assets/ folder
// exists on disk (Editor play mode, or a dev build with source alongside
// it) - not in a shipped build. Before shipping, move manifests+heightmaps
// into Resources or StreamingAssets and swap the lookup bodies for a
// Resources.Load / StreamingAssets read; Load()'s parsing, validation and
// terrain-building don't need to change.

using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;

public class ManifestSceneLoader : MonoBehaviour
{
    [SerializeField] private TMP_Text loadingText;

    [Tooltip("Scene/patch id to load, e.g. \"patch_43\" or \"pyramid\". Matches a file named \"<id>_manifest.json\", or a \"manifest.json\" inside a folder named <id>.")]
    [SerializeField] private string sceneId = "patch_43";

    [Tooltip("Folder (relative to Assets/) to search for manifests.")]
    [SerializeField] private string searchRoot = "Terrain";

    public TerrainSceneManifest LoadedManifest { get; private set; }

    // Terrain GameObject created by this loader.
    private GameObject currentTerrainGO;

    // TerrainData created by this loader.
    // This is the ONLY TerrainData that this script explicitly destroys.
    private TerrainData currentTerrainData;

    // Keeps track of the currently running load operation so a new scene
    // selection can cancel the previous one.
    private Coroutine activeLoadCoroutine;

    void Start()
    {
        Load(sceneId);
    }

    /// <summary>
    /// Starts loading the requested scene and displays the loading state.
    /// </summary>
    public void Load(string id)
    {
        // Stop an older load if the user selects another scene quickly.
        if (activeLoadCoroutine != null)
        {
            StopCoroutine(activeLoadCoroutine);
            activeLoadCoroutine = null;
        }

        activeLoadCoroutine = StartCoroutine(LoadSceneRoutine(id));
    }

    private IEnumerator LoadSceneRoutine(string id)
    {
        ShowLoading();

        // Give Unity one frame to actually render the loading message
        // before the synchronous terrain-building work begins.
        yield return null;

        try
        {
            sceneId = id;

            string manifestPath = FindManifestPath(id);

            if (manifestPath == null)
            {
                Debug.LogWarning(
                    $"ManifestSceneLoader: no manifest found for scene id '{id}' under Assets/{searchRoot}.");

                yield break;
            }

            // TerrainSceneManifest.Parse, not JsonUtility.FromJson directly -
            // see that method's header for why.
            TerrainSceneManifest manifest =
                TerrainSceneManifest.Parse(File.ReadAllText(manifestPath));

            ManifestValidator.ValidateOrThrow(manifest);

            LoadedManifest = manifest;
            LogManifest(manifestPath, manifest);

            string heightmapPath = FindHeightmapPath(manifestPath, id);

            if (heightmapPath == null)
            {
                Debug.LogWarning(
                    $"ManifestSceneLoader: manifest found for '{id}' but no matching heightmap PNG sits next to it " +
                    $"(looked for '{id}_heightmap.png' and 'heightmap.png' in '{Path.GetDirectoryName(manifestPath)}'). " +
                    $"NOTE: manifest.heightmap ('{manifest.heightmap}') is Team A's original delivery path, not a " +
                    "project-relative one - it can't be used directly to find the file. Terrain was not rebuilt.");

                yield break;
            }

            BuildTerrainFromManifest(id, manifest, heightmapPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError(
                $"ManifestSceneLoader: failed to load scene '{id}': {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            HideLoading();
            activeLoadCoroutine = null;
        }
    }

    private void ShowLoading()
    {
        if (loadingText == null)
            return;

        loadingText.text = "Loading...";
        loadingText.gameObject.SetActive(true);
    }

    private void HideLoading()
    {
        if (loadingText == null)
            return;

        loadingText.gameObject.SetActive(false);
    }

    void BuildTerrainFromManifest(
        string id,
        TerrainSceneManifest manifest,
        string heightmapPath)
    {
        ushort[,] raw16;
        int srcW;
        int srcH;

        try
        {
            raw16 = TerrainBuildUtility.LoadGrayscale16Png(
                heightmapPath,
                out srcW,
                out srcH);
        }
        catch (System.Exception e)
        {
            Debug.LogError(
                $"ManifestSceneLoader: failed to decode heightmap '{heightmapPath}': {e.Message}. Terrain was not rebuilt.");
            return;
        }

        int resolution =
            TerrainBuildUtility.PickHeightmapResolution(
                Mathf.Max(srcW, srcH));

        float[,] heights =
            TerrainBuildUtility.BuildHeightsArray(
                raw16,
                srcW,
                srcH,
                resolution);

        float elevationMin =
            manifest.elevation_range_m != null
                ? manifest.elevation_range_m.min
                : 0f;

        float elevationMax =
            manifest.elevation_range_m != null
                ? manifest.elevation_range_m.max
                : 1f;

        float worldWidth =
            manifest.real_world_size_m != null
                ? manifest.real_world_size_m.width
                : srcW;

        float worldLength =
            manifest.real_world_size_m != null
                ? manifest.real_world_size_m.height
                : srcH;

        // -------------------------------------------------------------
        // CLEAN UP OLD TERRAIN
        // -------------------------------------------------------------
        //
        // IMPORTANT:
        // Existing terrains in the scene may reference TerrainData assets
        // serialized by Unity. Destroying those TerrainData objects causes:
        //
        // "Destroying assets is not permitted to avoid data loss."
        //
        // Therefore:
        //   1. Destroy the old Terrain GameObjects.
        //   2. ONLY destroy the TerrainData that THIS loader created.

        Terrain[] existingTerrains =
            Object.FindObjectsByType<Terrain>(
                FindObjectsSortMode.None);

        foreach (Terrain t in existingTerrains)
        {
            if (t == null)
                continue;

            // If this is our previously-created runtime terrain,
            // remove its GameObject here. Its TerrainData is handled below.
            Destroy(t.gameObject);
        }

        // The TerrainData created by our previous runtime load is not a
        // project asset, so it is safe to destroy explicitly.
        if (currentTerrainData != null)
        {
            Destroy(currentTerrainData);
            currentTerrainData = null;
        }

        currentTerrainGO = null;

        // -------------------------------------------------------------
        // BUILD NEW TERRAIN
        // -------------------------------------------------------------

        string terrainName = $"{id}_Terrain";

        TerrainData newTerrainData;

        currentTerrainGO =
            TerrainBuildUtility.CreateTerrainGameObject(
                heights,
                resolution,
                worldWidth,
                worldLength,
                elevationMin,
                elevationMax,
                terrainName,
                out newTerrainData);

        // Remember the runtime TerrainData so we can safely dispose of it
        // on the next scene switch.
        currentTerrainData = newTerrainData;

        Debug.Log(
            $"ManifestSceneLoader: built '{terrainName}' at runtime from '{heightmapPath}' " +
            $"({srcW}x{srcH} -> resolution {resolution}). Size {worldWidth}m x {elevationMax - elevationMin}m x {worldLength}m, " +
            $"elevation {elevationMin}m to {elevationMax}m.");

        // Terrain.activeTerrain now points at the new terrain.
        // Re-frame the orbit camera.
        OrbitFlyCamera cam =
            Object.FindFirstObjectByType<OrbitFlyCamera>();

        if (cam != null)
            cam.Recenter();
    }

    string FindManifestPath(string id)
    {
        string root =
            Path.Combine(Application.dataPath, searchRoot);

        if (!Directory.Exists(root))
            return null;

        // Preferred convention:
        // "<id>_manifest.json" anywhere under the search root.

        foreach (string candidate in Directory.GetFiles(
            root,
            $"{id}_manifest.json",
            SearchOption.AllDirectories))
        {
            return candidate;
        }

        // Fallback convention:
        // generic "manifest.json" inside a folder named after the id.

        foreach (string candidate in Directory.GetFiles(
            root,
            "manifest.json",
            SearchOption.AllDirectories))
        {
            string folder =
                Path.GetFileName(
                    Path.GetDirectoryName(candidate)
                    ?? string.Empty);

            if (string.Equals(
                folder,
                id,
                System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    string FindHeightmapPath(
        string manifestPath,
        string id)
    {
        string dir =
            Path.GetDirectoryName(manifestPath);

        if (dir == null)
            return null;

        // Preferred convention:
        // "<id>_heightmap.png" next to "<id>_manifest.json".

        string preferred =
            Path.Combine(
                dir,
                $"{id}_heightmap.png");

        if (File.Exists(preferred))
            return preferred;

        // Fallback convention:
        // generic "heightmap.png" next to "manifest.json".

        string fallback =
            Path.Combine(
                dir,
                "heightmap.png");

        if (File.Exists(fallback))
            return fallback;

        return null;
    }

    void LogManifest(
        string path,
        TerrainSceneManifest m)
    {
        Debug.Log(
            $"ManifestSceneLoader: loaded '{path}'\n" +
            $"  id: {m.id}\n" +
            $"  source_image: {m.source_image}\n" +
            $"  heightmap: {m.heightmap} ({m.heightmap_encoding}, {m.heightmap_width_px}x{m.heightmap_height_px}px)\n" +
            $"  mesh: {m.mesh_width_px}x{m.mesh_height_px}px, subsample x{m.mesh_subsample_factor}\n" +
            $"  elevation_range_m: {m.elevation_range_m?.min} - {m.elevation_range_m?.max}\n" +
            $"  real_world_size_m: {m.real_world_size_m?.width} x {m.real_world_size_m?.height}\n" +
            $"  reference_available: {m.reference_available}");
    }
}