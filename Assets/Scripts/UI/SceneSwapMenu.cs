// SceneSwapMenu.cs
//
// Phase 3 UI: builds a simple runtime button list, one button per available
// manifest under Assets/<searchRoot>, and calls the existing
// ManifestSceneLoader.Load(id) when clicked (wrapped in a LoadingOverlay so
// the swap shows a loading state instead of a silent hitch - see
// LoadingOverlay.cs). This script only discovers ids and calls Load() - it
// does not touch ManifestSceneLoader's internal lookup/swap/dispose logic
// at all.
//
// The id-discovery scan below intentionally mirrors
// ManifestSceneLoader.FindManifestPath's two conventions (see that file)
// rather than modifying ManifestSceneLoader to expose a "list ids" method:
//   - preferred: "<id>_manifest.json" anywhere under the search root
//   - fallback: a generic "manifest.json" inside a folder named <id>
// It just enumerates every id found instead of stopping at the first match
// for one given id.
//
// EDITOR/DEV-ONLY, same caveat as ManifestSceneLoader: this scans
// Application.dataPath directly, so it only finds manifests in Editor Play
// mode or a dev build with Assets/ alongside it - not a shipped build (see
// ManifestSceneLoader.cs header for the Resources/StreamingAssets migration
// note that applies here too).
//
// No prefab wiring required: drop this component on any GameObject in the
// scene (the Canvas itself is fine) and it builds its own button list under
// a Canvas at Start.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SceneSwapMenu : MonoBehaviour
{
    [Tooltip("The ManifestSceneLoader to call Load(id) on. Auto-found if left empty.")]
    [SerializeField] private ManifestSceneLoader loader;

    [Tooltip("Canvas to parent the button list under. Auto-found if left empty.")]
    [SerializeField] private Canvas targetCanvas;

    [Tooltip("Folder (relative to Assets/) to search for manifests - mirrors ManifestSceneLoader's own searchRoot.")]
    [SerializeField] private string searchRoot = "Terrain";

    [Tooltip("Anchored position (from the Canvas's top-left) of the button list's container.")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(16f, -16f);

    [Tooltip("Shown for the duration of each swap so it never looks frozen. Auto-found, or added to this GameObject, if left empty.")]
    [SerializeField] private LoadingOverlay overlay;

    readonly List<Button> spawnedButtons = new List<Button>();

    void Start()
    {
        if (loader == null) loader = Object.FindFirstObjectByType<ManifestSceneLoader>();
        if (targetCanvas == null) targetCanvas = Object.FindFirstObjectByType<Canvas>();
        if (overlay == null) overlay = Object.FindFirstObjectByType<LoadingOverlay>();
        if (overlay == null) overlay = gameObject.AddComponent<LoadingOverlay>();

        if (loader == null || targetCanvas == null)
        {
            Debug.LogWarning("SceneSwapMenu: no ManifestSceneLoader or Canvas found in the scene - scene-swap buttons not built.");
            return;
        }

        List<string> ids = FindAvailableSceneIds();
        if (ids.Count == 0)
        {
            Debug.LogWarning($"SceneSwapMenu: no manifests found under Assets/{searchRoot} - scene-swap buttons not built.");
            return;
        }

        BuildButtonList(ids);
    }

    List<string> FindAvailableSceneIds()
    {
        var ids = new List<string>();
        string root = Path.Combine(Application.dataPath, searchRoot);
        if (!Directory.Exists(root)) return ids;

        const string suffix = "_manifest.json";
        foreach (string path in Directory.GetFiles(root, $"*{suffix}", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(path);
            string id = fileName.Substring(0, fileName.Length - suffix.Length);
            if (!ids.Contains(id)) ids.Add(id);
        }

        foreach (string path in Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
            if (!string.IsNullOrEmpty(folder) && !ids.Contains(folder)) ids.Add(folder);
        }

        ids.Sort();
        return ids;
    }

    void BuildButtonList(List<string> ids)
    {
        GameObject panelGO = new GameObject("SceneSwapPanel", typeof(RectTransform));
        panelGO.transform.SetParent(targetCanvas.transform, false);

        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = anchoredPosition;
        panelRect.sizeDelta = new Vector2(220f, 0f);

        Image panelBg = panelGO.AddComponent<Image>();
        panelBg.color = new Color(0f, 0f, 0f, 0.55f);

        VerticalLayoutGroup layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panelGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (string id in ids)
        {
            spawnedButtons.Add(CreateButton(panelGO.transform, id));
        }
    }

    Button CreateButton(Transform parent, string id)
    {
        GameObject buttonGO = new GameObject($"Btn_{id}", typeof(RectTransform));
        buttonGO.transform.SetParent(parent, false);

        LayoutElement le = buttonGO.AddComponent<LayoutElement>();
        le.preferredHeight = 32f;

        Image bg = buttonGO.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.15f);

        Button button = buttonGO.AddComponent<Button>();
        button.targetGraphic = bg;

        GameObject labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(buttonGO.transform, false);
        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 2f);
        labelRect.offsetMax = new Vector2(-8f, -2f);

        TMP_Text label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = id;
        label.fontSize = 16f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = Color.white;
        label.raycastTarget = false;

        // Captures id by value for the closure. The only call this makes
        // into ManifestSceneLoader is loader.Load(id) - its swap/dispose
        // logic is untouched; the overlay/coroutine just wraps that call
        // so the swap shows a loading state instead of a silent hitch.
        button.onClick.AddListener(() => StartCoroutine(SwapTo(id)));

        return button;
    }

    IEnumerator SwapTo(string id)
    {
        SetButtonsInteractable(false);
        yield return overlay.RunWithOverlay(() => loader.Load(id));
        SetButtonsInteractable(true);
    }

    void SetButtonsInteractable(bool interactable)
    {
        foreach (Button b in spawnedButtons)
        {
            if (b != null) b.interactable = interactable;
        }
    }
}
