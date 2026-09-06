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
using System;

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
}
