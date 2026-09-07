// MetadataPanel.cs
//
// Phase 3 UI: small panel showing terrain_type, RMSE and MAE for whichever
// manifest ManifestSceneLoader currently has loaded. Read-only - the only
// thing this script touches on ManifestSceneLoader is reading its public
// LoadedManifest property, so it needs no wiring to SceneSwapMenu or
// LoadingOverlay and stays correct after every swap on its own (it polls
// LoadedManifest each frame and only redraws when the reference changes).
//
// Team A has not shipped real terrain_type/accuracy_metrics values yet -
// every manifest in the repo today omits both, so JsonUtility leaves them
// at their C# default (null string / null AccuracyMetrics; see
// TerrainSceneManifest.cs). This panel treats that as "not provided yet"
// and shows "Pending"/"-" instead of a fabricated number or a crash. The
// accuracy_metrics field's JSON key names are a placeholder guess until
// Team A's real schema lands (see TerrainSceneManifest.cs) - if their
// actual keys differ, this panel will just keep showing "-" until that
// file's field names are updated to match, never throw.
//
// No prefab wiring required: drop this component on any GameObject in the
// scene (the Canvas itself is fine) and it builds its own panel at Start.
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MetadataPanel : MonoBehaviour
{
    [Tooltip("The ManifestSceneLoader to read LoadedManifest from. Auto-found if left empty.")]
    [SerializeField] private ManifestSceneLoader loader;

    [Tooltip("Canvas to parent the panel under. Auto-found if left empty.")]
    [SerializeField] private Canvas targetCanvas;

    [Tooltip("Anchored position (from the Canvas's top-right) of the panel.")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(-16f, -16f);

    TMP_Text bodyText;
    TerrainSceneManifest lastManifest;
    bool lastManifestSeen; // distinguishes "no manifest yet" from "same null manifest as last frame"

    void Start()
    {
        if (loader == null) loader = Object.FindFirstObjectByType<ManifestSceneLoader>();
        if (targetCanvas == null) targetCanvas = Object.FindFirstObjectByType<Canvas>();

        if (loader == null || targetCanvas == null)
        {
            Debug.LogWarning("MetadataPanel: no ManifestSceneLoader or Canvas found in the scene - metadata panel not built.");
            return;
        }

        BuildPanel();
        Refresh();
    }

    void Update()
    {
        if (bodyText == null || loader == null) return;

        if (!lastManifestSeen || !ReferenceEquals(loader.LoadedManifest, lastManifest))
        {
            Refresh();
        }
    }

    void Refresh()
    {
        lastManifest = loader.LoadedManifest;
        lastManifestSeen = true;
        TerrainSceneManifest m = lastManifest;

        string terrainType = (m != null && !string.IsNullOrEmpty(m.terrain_type)) ? m.terrain_type : "Pending";
        string rmse = m?.accuracy_metrics != null ? $"{m.accuracy_metrics.rmse:F2} m" : "—";
        string mae = m?.accuracy_metrics != null ? $"{m.accuracy_metrics.mae:F2} m" : "—";

        bodyText.text =
            $"Terrain Type: {terrainType}\n" +
            $"RMSE: {rmse}\n" +
            $"MAE: {mae}";
    }

    void BuildPanel()
    {
        GameObject panelGO = new GameObject("MetadataPanel", typeof(RectTransform));
        panelGO.transform.SetParent(targetCanvas.transform, false);

        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = anchoredPosition;
        panelRect.sizeDelta = new Vector2(240f, 0f);

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
        le.preferredHeight = 64f;

        bodyText = textGO.AddComponent<TextMeshProUGUI>();
        bodyText.fontSize = 15f;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.color = Color.white;
        bodyText.raycastTarget = false;
        bodyText.text = "Terrain Type: Pending\nRMSE: —\nMAE: —";
    }
}
