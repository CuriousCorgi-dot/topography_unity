// TerrainSceneManifest.cs
//
// Plain data mirror of the manifest.json schema Team A ships alongside each
// heightmap (see Assets/Terrain/Heightmaps/patch_43_manifest.json and
// Assets/Terrain/PyramidTest/pyramid_manifest.json for the two shapes in the
// repo today). Field names match the JSON keys exactly (including the
// snake_case) because JsonUtility maps by name with no remapping support.
//
// Not every manifest populates every field (the pyramid test manifest has no
// mesh_width_px/mesh_height_px, for instance) - JsonUtility just leaves
// those at their C# default, so treat 0/null on those as "not provided"
// rather than a parse failure.
//
// CAVEAT that only applies to nested [Serializable] class fields (currently
// just accuracy_metrics): JsonUtility's native serializer always allocates
// a non-null instance for those, even when the key is completely absent
// from the source JSON - it has no representation for "this compound field
// is null" and needs a fully-populated object graph. Only that nested
// object's own leaf values (rmse/mae) actually stay at their type default
// (0) when absent; the object reference itself is never null coming out of
// JsonUtility.FromJson. Use TerrainSceneManifest.Parse (not
// JsonUtility.FromJson directly) to get a real null there when the key
// truly wasn't in the JSON.
using System;
using UnityEngine;

[Serializable]
public class ElevationRangeMeters
{
    public float min;
    public float max;
}

[Serializable]
public class RealWorldSizeMeters
{
    public float width;
    public float height;
}

// Team A has not started shipping accuracy_metrics yet (tracked in the
// Phase 3 brief as blocked on their side). "rmse"/"mae" here are a
// best-effort guess at the eventual JSON key names, not a confirmed
// schema - if their real keys differ once delivered, JsonUtility will
// just leave this field null (same as today), so MetadataPanel keeps
// showing "Pending"/"-" instead of crashing; update these field names to
// match once Team A's actual manifest shape is known.
[Serializable]
public class AccuracyMetrics
{
    public float rmse;
    public float mae;
}

[Serializable]
public class TerrainSceneManifest
{
    public string id;
    public string source_image;
    public string heightmap;
    public string heightmap_encoding;
    public int heightmap_width_px;
    public int heightmap_height_px;
    public int mesh_width_px;
    public int mesh_height_px;
    public int mesh_subsample_factor;
    public ElevationRangeMeters elevation_range_m;
    public bool reference_available;
    public RealWorldSizeMeters real_world_size_m;

    // Not yet delivered by Team A - absent from every manifest in the repo
    // today. terrain_type (a plain string) correctly stays null when
    // absent; accuracy_metrics does NOT (see the JsonUtility caveat above) -
    // always go through Parse() below rather than JsonUtility.FromJson
    // directly, or accuracy_metrics will read as a real all-zero result
    // instead of "not provided". Treat null/empty as "not provided yet",
    // not a parse failure - see MetadataPanel.cs.
    public string terrain_type;
    public AccuracyMetrics accuracy_metrics;

    /// <summary>
    /// Parses manifest JSON exactly like JsonUtility.FromJson, with one
    /// correction: JsonUtility always allocates a non-null accuracy_metrics
    /// instance even when that key is absent from the JSON (see the class
    /// header's caveat), so a plain null-check on it can never distinguish
    /// "genuinely all-zero metrics were reported" from "the key was never
    /// in the source JSON at all". This does an explicit raw-text presence
    /// check for the "accuracy_metrics" key and nulls the field back out
    /// if it truly wasn't there.
    /// </summary>
    public static TerrainSceneManifest Parse(string json)
    {
        TerrainSceneManifest manifest = JsonUtility.FromJson<TerrainSceneManifest>(json);
        if (manifest != null && manifest.accuracy_metrics != null &&
            (json == null || !json.Contains("\"accuracy_metrics\"")))
        {
            manifest.accuracy_metrics = null;
        }
        return manifest;
    }
}
