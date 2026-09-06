using System;
using UnityEngine;

/// <summary>
/// Validates a TerrainSceneManifest before the terrain pipeline consumes it.
/// Required fields are fatal; fields that are legitimately optional in the
/// current manifest schema are only validated when supplied.
/// </summary>
public static class ManifestValidator
{
    public static void ValidateOrThrow(TerrainSceneManifest manifest)
    {
        if (manifest == null)
        {
            throw new ManifestValidationException("Manifest is null.");
        }

        if (string.IsNullOrWhiteSpace(manifest.id))
        {
            throw new ManifestValidationException("Manifest id is missing.");
        }

        if (string.IsNullOrWhiteSpace(manifest.heightmap))
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' is missing heightmap.");
        }

        if (string.IsNullOrWhiteSpace(manifest.heightmap_encoding))
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' is missing heightmap_encoding.");
        }

        if (manifest.heightmap_width_px <= 0 ||
            manifest.heightmap_height_px <= 0)
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' has invalid heightmap dimensions: " +
                $"{manifest.heightmap_width_px}x{manifest.heightmap_height_px}.");
        }

        if (manifest.elevation_range_m == null)
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' is missing elevation_range_m.");
        }

        if (!IsFinite(manifest.elevation_range_m.min) ||
            !IsFinite(manifest.elevation_range_m.max))
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' has non-finite elevation values.");
        }

        if (manifest.elevation_range_m.max <=
            manifest.elevation_range_m.min)
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' has invalid elevation range: " +
                $"{manifest.elevation_range_m.min} to " +
                $"{manifest.elevation_range_m.max}.");
        }

        if (manifest.real_world_size_m == null)
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' is missing real_world_size_m.");
        }

        if (!IsFinite(manifest.real_world_size_m.width) ||
            !IsFinite(manifest.real_world_size_m.height) ||
            manifest.real_world_size_m.width <= 0f ||
            manifest.real_world_size_m.height <= 0f)
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' has invalid real-world dimensions: " +
                $"{manifest.real_world_size_m.width} x " +
                $"{manifest.real_world_size_m.height}.");
        }

        // Mesh dimensions are optional in the current schema.
        // If supplied, they must be positive.
        if (manifest.mesh_width_px < 0 ||
            manifest.mesh_height_px < 0)
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' has invalid mesh dimensions.");
        }

        if (manifest.mesh_subsample_factor < 0)
        {
            throw new ManifestValidationException(
                $"Manifest '{manifest.id}' has invalid mesh_subsample_factor: " +
                $"{manifest.mesh_subsample_factor}.");
        }

        Debug.Log(
            $"[ManifestValidator] Manifest '{manifest.id}' validated successfully.");
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

public class ManifestValidationException : Exception
{
    public ManifestValidationException(string message)
        : base(message)
    {
    }
}