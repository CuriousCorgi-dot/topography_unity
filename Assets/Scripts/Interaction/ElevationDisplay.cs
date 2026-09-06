// ElevationDisplay.cs
//
// Live "you are pointing at N meters" readout: raycasts from the camera
// through the cursor and, if the ray lands on a real Terrain's own
// TerrainCollider, converts the hit into real-world meters using the same
// formula Team A's manifests define:
//   elevation = elevation_range_m.min + heightRatio * (max - min)
//
// KNOWN BUG RISK THIS GUARDS AGAINST (flagged in the brief): a flat
// stand-in collider - a placeholder ground plane, this scene's own lake
// water plane, anything sitting in front of the terrain's real
// heightmap-shaped collider - would make every reading flat and wrong with
// no error at all. This script only accepts hits whose collider is
// literally a TerrainCollider; anything else clears the readout instead of
// showing a plausible-looking wrong number. (DepthWizardSceneSetup's water
// plane has its own Collider removed for exactly this reason.)
//
// Reusable across scenes: prefers elevation_range_m from a ManifestSceneLoader
// in the scene (so it reads the real per-patch numbers Team A ships), and
// falls back to deriving the same numbers from the terrain's own transform/
// size if no loader is present - DepthWizardTerrainBuilder places every
// terrain's Y at elevation_range_m.min and sizes it to the span, so the two
// sources agree whenever both are available.
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class ElevationDisplay : MonoBehaviour
{
    [Tooltip("Text this script writes the live elevation into. Auto-fills from a TMP_Text on this same GameObject if left empty.")]
    [SerializeField] private TMP_Text readoutText;

    [Tooltip("Camera to raycast from. Leave empty to use Camera.main.")]
    [SerializeField] private Camera sourceCamera;

    [SerializeField] private float maxRayDistance = 100000f;

    [Tooltip("Reads elevation_range_m from this scene's manifest loader instead of the terrain's own transform/size. Auto-found if left empty.")]
    [SerializeField] private ManifestSceneLoader manifestLoader;

    void Awake()
    {
        if (readoutText == null) readoutText = GetComponent<TMP_Text>();
        if (sourceCamera == null) sourceCamera = Camera.main;
        if (manifestLoader == null) manifestLoader = Object.FindFirstObjectByType<ManifestSceneLoader>();
    }

    void Update()
    {
        if (readoutText == null) return;

        Camera cam = sourceCamera != null ? sourceCamera : Camera.main;
        if (cam == null)
        {
            readoutText.text = "Elevation: -- m";
            return;
        }

        Vector2 screenPos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Ray ray = cam.ScreenPointToRay(screenPos);

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance) && hit.collider is TerrainCollider)
        {
            Terrain terrain = hit.collider.GetComponent<Terrain>();
            readoutText.text = $"Elevation: {ComputeElevationMeters(terrain, hit.point):F1} m";
        }
        else
        {
            readoutText.text = "Elevation: -- m";
        }
    }

    float ComputeElevationMeters(Terrain terrain, Vector3 worldPoint)
    {
        float heightRatio = Mathf.Clamp01((worldPoint.y - terrain.transform.position.y) / terrain.terrainData.size.y);

        // Fallback: DepthWizardTerrainBuilder sets transform.position.y to
        // elevation_range_m.min and terrainData.size.y to the elevation
        // span, so these already equal the manifest's numbers unless
        // overridden below.
        float elevationMin = terrain.transform.position.y;
        float elevationRange = terrain.terrainData.size.y;

        TerrainSceneManifest manifest = manifestLoader != null ? manifestLoader.LoadedManifest : null;
        if (manifest?.elevation_range_m != null)
        {
            elevationMin = manifest.elevation_range_m.min;
            elevationRange = manifest.elevation_range_m.max - manifest.elevation_range_m.min;
        }

        return elevationMin + heightRatio * elevationRange;
    }
}
