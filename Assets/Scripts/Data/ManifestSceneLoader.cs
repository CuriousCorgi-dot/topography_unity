// ManifestSceneLoader.cs
//
// Stub for the real scene-loading pipeline: given a scene/patch id, finds
// that patch's manifest.json under Assets/Terrain and logs its fields. This
// is intentionally not wired up to actually swap terrain/texture/data yet -
// that comes once the terrain-import and texture-draping pieces this stub
// will eventually call are in place. Nothing here is hardcoded to patch_43:
// point sceneId at any id and it finds that manifest by the same two lookup
// rules Team A's files already follow.
//
// EDITOR/DEV-ONLY FILE LOOKUP: FindManifestPath walks Application.dataPath
// directly, which only works when the Assets/ folder exists on disk (Editor
// play mode, or a dev build with source alongside it) - not in a shipped
// build. Before shipping, move manifests into Resources or StreamingAssets
// and swap FindManifestPath's body for a Resources.Load /
// StreamingAssets read; Load()'s parsing and logging don't need to change.
using System.IO;
using UnityEngine;

public class ManifestSceneLoader : MonoBehaviour
{
    [Tooltip("Scene/patch id to load, e.g. \"patch_43\" or \"pyramid\". Matches a file named \"<id>_manifest.json\", or a \"manifest.json\" inside a folder named <id>.")]
    [SerializeField] private string sceneId = "patch_43";

    [Tooltip("Folder (relative to Assets/) to search for manifests.")]
    [SerializeField] private string searchRoot = "Terrain";

    public TerrainSceneManifest LoadedManifest { get; private set; }

    void Start()
    {
        Load(sceneId);
    }

    /// <summary>Finds and parses the manifest for the given scene id, and logs its fields.</summary>
    public void Load(string id)
    {
        sceneId = id;
        string path = FindManifestPath(id);
        if (path == null)
        {
            Debug.LogWarning($"ManifestSceneLoader: no manifest found for scene id '{id}' under Assets/{searchRoot}.");
            return;
        }

	TerrainSceneManifest manifest =
    		JsonUtility.FromJson<TerrainSceneManifest>(File.ReadAllText(path));

	ManifestValidator.ValidateOrThrow(manifest);

	LoadedManifest = manifest;
	LogManifest(path, manifest);

        // TODO (later phase): use manifest.heightmap / source_image /
        // elevation_range_m / real_world_size_m here to load and swap in the
        // matching terrain, draped texture and collider for `id`.
    }

    string FindManifestPath(string id)
    {
        string root = Path.Combine(Application.dataPath, searchRoot);
        if (!Directory.Exists(root)) return null;

        // Preferred convention (what's already in this repo): "<id>_manifest.json" anywhere under the search root.
        foreach (string candidate in Directory.GetFiles(root, $"{id}_manifest.json", SearchOption.AllDirectories))
        {
            return candidate;
        }

        // Fallback convention (matches Team A's raw delivery layout): a generic "manifest.json" inside a folder named after the id.
        foreach (string candidate in Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(candidate) ?? string.Empty);
            if (string.Equals(folder, id, System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    void LogManifest(string path, TerrainSceneManifest m)
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
