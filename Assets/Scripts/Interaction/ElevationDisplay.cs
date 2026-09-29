// ElevationDisplay.cs
//
// Bottom-left runtime-built stats panel (same self-building pattern as
// MetadataPanel.cs, just the opposite corner). Shows four lines:
//
//   Cursor Elevation - what's under the mouse right now. Raycasts from the
//   camera through the cursor and only accepts a hit whose collider is
//   literally a TerrainCollider - a flat stand-in collider (this scene's
//   lake water plane, any placeholder ground) would make every reading
//   flat and wrong with no error at all, so anything else clears the
//   readout instead of showing a plausible-looking wrong number.
//   (DepthWizardSceneSetup's water plane has its own Collider removed for
//   exactly this reason.)
//
//   Camera Elevation - the camera's own height, using the same
//   world-Y-to-real-world-meters formula as the cursor reading, so the two
//   numbers are directly comparable (not "one's real-world meters, one's
//   raw Unity Y").
//
//   Patch Size / Elevation Range - static per-patch context pulled
//   straight from the currently loaded manifest, refreshed on every scene
//   swap.
//
// Reusable across scenes: prefers elevation_range_m/real_world_size_m from
// a ManifestSceneLoader in the scene (so it reads the real per-patch
// numbers Team A ships), and falls back to deriving the elevation numbers
// from the terrain's own transform/size if no loader is present -
// DepthWizardTerrainBuilder places every terrain's Y at elevation_range_m.min
// and sizes it to the span, so the two sources agree whenever both are
// available. Patch size has no such fallback (it isn't recoverable from the
// terrain alone) and just shows "-" without a manifest.
//
// No prefab wiring required: drop this component on any GameObject in the
// scene (the Canvas itself is fine, same as MetadataPanel) and it builds
// its own panel at Start. If a readoutText is still assigned in the
// Inspector from before this rewrite, that old manually-placed Text object
// is no longer used by this script - safe to delete it from the scene.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class ElevationDisplay : MonoBehaviour
{
    [Tooltip("Camera to raycast from. Leave empty to use Camera.main.")]
    [SerializeField] private Camera sourceCamera;

    [SerializeField] private float maxRayDistance = 100000f;

    [Tooltip("Reads elevation_range_m/real_world_size_m from this scene's manifest loader instead of the terrain's own transform/size. Auto-found if left empty.")]
    [SerializeField] private ManifestSceneLoader manifestLoader;

    [Tooltip("Canvas to parent the panel under. Auto-found if left empty.")]
    [SerializeField] private Canvas targetCanvas;

    [Tooltip("Anchored position (from the Canvas's bottom-left) of the panel.")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(16f, 16f);

    [Tooltip("Font size of the four stat lines. Panel width scales with this automatically.")]
    [SerializeField] private float fontSize = 22f;

    TMP_Text bodyText;
    TerrainSceneManifest lastManifest;
    bool lastManifestSeen;

    void Awake()
    {
        if (sourceCamera == null) sourceCamera = Camera.main;
        if (manifestLoader == null) manifestLoader = Object.FindFirstObjectByType<ManifestSceneLoader>();
    }

    void Start()
    {
        if (targetCanvas == null) targetCanvas = Object.FindFirstObjectByType<Canvas>();

        if (targetCanvas == null)
        {
            Debug.LogWarning("ElevationDisplay: no Canvas found in the scene - stats panel not built.");
            return;
        }

        BuildPanel();
    }

    void Update()
    {
        if (bodyText == null) return;

        // Static per-patch lines only need refreshing when the manifest
        // actually changes (on a scene swap), not every frame - but the
        // live lines (cursor/camera elevation) do need every-frame work,
        // so this just rebuilds the whole four-line string each frame
        // anyway; string building four short lines a frame is cheap and
        // keeps this method simple rather than splitting cached vs live
        // text into two separate code paths.

        if (!lastManifestSeen || !ReferenceEquals(manifestLoader?.LoadedManifest, lastManifest))
        {
            lastManifest = manifestLoader != null ? manifestLoader.LoadedManifest : null;
            lastManifestSeen = true;
        }

        string cursorLine = "Cursor Elevation: -- m";
        Camera cam = sourceCamera != null ? sourceCamera : Camera.main;

        if (cam != null)
        {
            Vector2 screenPos = Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Ray ray = cam.ScreenPointToRay(screenPos);

            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance) && hit.collider is TerrainCollider)
            {
                Terrain hitTerrain = hit.collider.GetComponent<Terrain>();
                cursorLine = $"Cursor Elevation: {ComputeElevationMeters(hitTerrain, hit.point):F1} m";
            }
        }

        string cameraLine = "Camera Elevation: -- m";
        Terrain activeTerrain = Terrain.activeTerrain;

        if (cam != null && activeTerrain != null)
        {
            cameraLine = $"Camera Elevation: {ComputeElevationMeters(activeTerrain, cam.transform.position):F1} m";
        }

        string sizeLine = "Patch Size: -";
        string rangeLine = "Elevation Range: -";

        if (lastManifest != null)
        {
            if (lastManifest.real_world_size_m != null)
            {
                sizeLine =
                    $"Patch Size: {lastManifest.real_world_size_m.width:F0} m \u00d7 {lastManifest.real_world_size_m.height:F0} m";
            }

            if (lastManifest.elevation_range_m != null)
            {
                rangeLine =
                    $"Elevation Range: {lastManifest.elevation_range_m.min:F0}\u2013{lastManifest.elevation_range_m.max:F0} m";
            }
        }

        bodyText.text = $"{cursorLine}\n{cameraLine}\n{sizeLine}\n{rangeLine}";
    }

    float ComputeElevationMeters(Terrain terrain, Vector3 worldPoint)
    {
        if (terrain == null) return 0f;

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

    void BuildPanel()
    {
        GameObject panelGO = new GameObject("ElevationStatsPanel", typeof(RectTransform));
        panelGO.transform.SetParent(targetCanvas.transform, false);

        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 0f);
        panelRect.pivot = new Vector2(0f, 0f);
        panelRect.anchoredPosition = anchoredPosition;
        // Width scales with font size so bigger text doesn't get clipped
        // or force-wrapped - 260 was tuned for the old fontSize of 15.
        panelRect.sizeDelta = new Vector2(260f * (fontSize / 15f), 0f);

        Image panelBg = panelGO.AddComponent<Image>();
        panelBg.color = new Color(0f, 0f, 0f, 0.55f);

        VerticalLayoutGroup layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panelGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject textGO = new GameObject("Body", typeof(RectTransform));
        textGO.transform.SetParent(panelGO.transform, false);

        LayoutElement le = textGO.AddComponent<LayoutElement>();
        // Four lines - scale preferred height with font size the same way,
        // 84 was tuned for the old fontSize of 15.
        le.preferredHeight = 84f * (fontSize / 15f);

        bodyText = textGO.AddComponent<TextMeshProUGUI>();
        bodyText.fontSize = fontSize;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.color = Color.white;
        bodyText.raycastTarget = false;
        bodyText.text = "Cursor Elevation: -- m\nCamera Elevation: -- m\nPatch Size: -\nElevation Range: -";
    }
}