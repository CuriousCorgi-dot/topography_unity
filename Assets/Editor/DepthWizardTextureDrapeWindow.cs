// DepthWizardTextureDrapeWindow.cs
//
// Small Editor window for Phase 2 step 5 (drape the source texture onto the
// terrain). A window instead of a plain menu item because it needs a
// Texture2D reference, which only a GUI field can supply - we have no way
// to know where any given scene's source .tif lives on disk. Import the
// .tif into the project as a Texture2D first (drag it anywhere under
// Assets/), then point this window at it.
using UnityEditor;
using UnityEngine;

public class DepthWizardTextureDrapeWindow : EditorWindow
{
    Terrain targetTerrain;
    Texture2D sourceTexture;
    bool flipVertically = true;

    [MenuItem("DepthWizard/Setup/Drape Terrain Texture...")]
    public static void Open()
    {
        var window = GetWindow<DepthWizardTextureDrapeWindow>(true, "DepthWizard - Drape Terrain Texture");
        window.targetTerrain = Terrain.activeTerrain;
        window.minSize = new Vector2(440, 180);
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Drapes a diffuse texture (Team A's source .tif, imported as a Texture2D) over a terrain as " +
            "its single Terrain Layer, tiled to exactly match that terrain's world size so it lines up " +
            "1:1 with the heightmap's footprint instead of repeating.",
            MessageType.Info);

        EditorGUILayout.Space();
        targetTerrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", targetTerrain, typeof(Terrain), true);
        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source Texture (.tif)", sourceTexture, typeof(Texture2D), false);
        flipVertically = EditorGUILayout.ToggleLeft(
            new GUIContent(
                "Flip vertically",
                "DepthWizardTerrainBuilder maps a heightmap PNG's row 0 to the terrain's Z=0 edge, but " +
                "Unity's texture importer flips images the opposite way on import. Leave this on unless " +
                "the drape looks upside-down (lake/ridgelines mirrored top-to-bottom) once applied."),
            flipVertically);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(targetTerrain == null || sourceTexture == null))
        {
            if (GUILayout.Button("Drape", GUILayout.Height(28)))
            {
                DepthWizardSceneSetup.DrapeTexture(targetTerrain, sourceTexture, flipVertically);
                Close();
            }
        }
    }
}
