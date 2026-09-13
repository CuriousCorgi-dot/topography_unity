// LoadingOverlay.cs
//
// Phase 3 UI: a full-screen dimmed overlay with a spinning ring and a
// "Loading terrain..." label, shown for the duration of a scene swap so
// the app never reads as frozen while ManifestSceneLoader.Load() runs.
// Built entirely at runtime (a procedural ring sprite included) - no
// prefab or texture assets required.
//
// ManifestSceneLoader.Load() is fire-and-forget: it starts its own
// coroutine and returns the Coroutine handle immediately, the actual
// manifest-parse/terrain-build work happens over the following frames.
// RunWithOverlay below relies on that returned handle - it shows the
// overlay, waits for it to actually render, starts the load via the given
// delegate, then "yield return"s the Coroutine it gets back so this
// coroutine genuinely waits for the async load to finish before hiding
// the overlay again (a bare "call it and hide" would hide/re-enable the
// UI while the terrain build was still in flight).
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoadingOverlay : MonoBehaviour
{
    [Tooltip("Canvas to parent the overlay under. Auto-found if left empty.")]
    [SerializeField] private Canvas targetCanvas;

    [Tooltip("Degrees per second the spinner ring rotates while visible.")]
    [SerializeField] private float spinSpeedDegPerSec = 320f;

    GameObject overlayGO;
    RectTransform spinnerRect;
    bool visible;

    void Awake()
    {
        if (targetCanvas == null) targetCanvas = Object.FindFirstObjectByType<Canvas>();
        if (targetCanvas != null) BuildOverlay();
    }

    void Update()
    {
        if (visible && spinnerRect != null)
        {
            spinnerRect.Rotate(0f, 0f, -spinSpeedDegPerSec * Time.unscaledDeltaTime);
        }
    }

    /// <summary>
    /// Shows the overlay, waits for it to actually render, calls
    /// <paramref name="startWork"/> to kick off the real work and waits for
    /// the Coroutine it returns to finish, then hides the overlay again.
    /// Hiding always runs (try/finally), including if starting the work
    /// throws before it ever returns a Coroutine.
    /// </summary>
    public IEnumerator RunWithOverlay(System.Func<Coroutine> startWork)
    {
        Show();

        // Let the overlay actually get presented before starting the work
        // below - otherwise Unity might never draw this frame and the
        // screen would just freeze with no visible transition at all.
        yield return null;
        yield return null;

        try
        {
            Coroutine work = startWork();
            if (work != null)
            {
                yield return work;
            }
        }
        finally
        {
            Hide();
        }
    }

    void Show()
    {
        if (overlayGO == null) return;
        visible = true;
        overlayGO.SetActive(true);
    }

    void Hide()
    {
        if (overlayGO == null) return;
        visible = false;
        overlayGO.SetActive(false);
    }

    void BuildOverlay()
    {
        overlayGO = new GameObject("LoadingOverlay", typeof(RectTransform));
        overlayGO.transform.SetParent(targetCanvas.transform, false);
        overlayGO.transform.SetAsLastSibling(); // draw on top of the scene-swap button panel

        RectTransform rect = overlayGO.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image fade = overlayGO.AddComponent<Image>();
        fade.color = new Color(0f, 0f, 0f, 0.6f);
        fade.raycastTarget = true; // also blocks clicks on the buttons behind it while loading

        GameObject spinnerGO = new GameObject("Spinner", typeof(RectTransform));
        spinnerGO.transform.SetParent(overlayGO.transform, false);
        spinnerRect = spinnerGO.GetComponent<RectTransform>();
        spinnerRect.anchorMin = new Vector2(0.5f, 0.5f);
        spinnerRect.anchorMax = new Vector2(0.5f, 0.5f);
        spinnerRect.pivot = new Vector2(0.5f, 0.5f);
        spinnerRect.anchoredPosition = Vector2.zero;
        spinnerRect.sizeDelta = new Vector2(64f, 64f);

        Image spinnerImage = spinnerGO.AddComponent<Image>();
        spinnerImage.sprite = BuildSpinnerSprite();
        spinnerImage.color = Color.white;
        spinnerImage.raycastTarget = false;

        GameObject labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(overlayGO.transform, false);
        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -48f);
        labelRect.sizeDelta = new Vector2(300f, 40f);

        TMP_Text label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = "Loading terrain...";
        label.fontSize = 20f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;

        overlayGO.SetActive(false);
    }

    // Procedural ring-with-a-gap sprite so this component needs zero
    // external texture assets to work. Built once and cached.
    static Sprite spinnerSpriteCache;
    static Sprite BuildSpinnerSprite()
    {
        if (spinnerSpriteCache != null) return spinnerSpriteCache;

        const int size = 64;
        const float outerR = 30f, innerR = 22f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(size / 2f, size / 2f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                float dist = p.magnitude;
                float angle = Mathf.Atan2(p.y, p.x); // -PI..PI
                float normalizedAngle = (angle + Mathf.PI) / (2f * Mathf.PI); // 0..1

                bool inRing = dist <= outerR && dist >= innerR;
                // Leave a gap so the ring reads as a spinner arc, not a full donut.
                bool inArc = normalizedAngle <= 0.75f;

                tex.SetPixel(x, y, (inRing && inArc) ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        }
        tex.Apply();

        spinnerSpriteCache = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return spinnerSpriteCache;
    }
}
