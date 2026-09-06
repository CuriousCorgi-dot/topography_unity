// TerrainBuildUtility.cs
//
// Shared, Editor-independent pieces of turning a 16-bit grayscale heightmap
// PNG into a Unity Terrain. Deliberately has no "using UnityEditor" anywhere
// in this file, so it can be called from both:
//   - DepthWizardTerrainBuilder (Assets/Editor, Editor-only): builds a
//     TerrainData and additionally saves it as a project asset via
//     AssetDatabase, for the one-click DepthWizard menu items.
//   - ManifestSceneLoader (Assets/Scripts, runtime): builds a TerrainData
//     that lives only in memory, so a scene can swap terrains while the
//     game is actually running (Play mode or a build) with no Editor
//     asset/import step involved at all - the Phase 3 roadmap item this
//     exists for ("Runtime loader in C#... so scenes swap while the app
//     runs, no Editor re-import").
//
// The PNG decoder below bypasses Unity's AssetImporter/Texture2D/GetPixels
// pipeline entirely - see DepthWizardTerrainBuilder's header comment for the
// full story on why (that route corrupts/silently downgrades a 16-bit
// heightmap). Reading the file's bytes directly here means this code has
// zero dependency on the asset being imported as a Texture2D at all, which
// is exactly what makes it safe to call at runtime, not just in the Editor.
using System;
using System.IO;
using UnityEngine;

public static class TerrainBuildUtility
{
    // Unity terrain heightmap resolution must be 2^n + 1 (33, 65, ... 4097).
    public static readonly int[] ValidHeightmapResolutions = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };

    public static int PickHeightmapResolution(int srcMax)
    {
        foreach (var r in ValidHeightmapResolutions)
            if (r >= srcMax) return r;
        return ValidHeightmapResolutions[ValidHeightmapResolutions.Length - 1];
    }

    /// <summary>
    /// Builds the [resolution,resolution] normalized (0-1) heights array
    /// TerrainData.SetHeights expects, resampling/clamping from a source
    /// heightmap of arbitrary size (edge pixels repeat if resolution is
    /// larger than the source - same as a clamp-to-edge sampler).
    /// </summary>
    public static float[,] BuildHeightsArray(ushort[,] raw16, int srcW, int srcH, int resolution)
    {
        float[,] heights = new float[resolution, resolution];
        for (int y = 0; y < resolution; y++)
        {
            int sy = Mathf.Min(y, srcH - 1);
            for (int x = 0; x < resolution; x++)
            {
                int sx = Mathf.Min(x, srcW - 1);
                heights[y, x] = raw16[sy, sx] / 65535f;
            }
        }
        return heights;
    }

    /// <summary>
    /// Creates a Terrain GameObject in the currently active scene from a
    /// heights array, sized/positioned per the given real-world extent and
    /// elevation range - the same placement convention every DepthWizard
    /// terrain uses: transform.position.y = elevationMin, size.y = the
    /// elevation span, so ElevationDisplay's fallback math (and anything
    /// else that reads a terrain's own transform/size instead of the
    /// manifest) still lines up.
    ///
    /// Does NOT touch AssetDatabase - the returned TerrainData lives only in
    /// memory. That's correct for a runtime swap; an Editor-only caller that
    /// wants the TerrainData saved as a visible project asset does that
    /// itself with the GameObject/TerrainData this returns.
    /// </summary>
    public static GameObject CreateTerrainGameObject(
        float[,] heights, int resolution, float worldWidth, float worldLength,
        float elevationMin, float elevationMax, string terrainName,
        out TerrainData terrainData)
    {
        terrainData = new TerrainData();
        terrainData.heightmapResolution = resolution;
        float elevationSpan = elevationMax - elevationMin;
        terrainData.size = new Vector3(worldWidth, elevationSpan, worldLength);
        terrainData.SetHeights(0, 0, heights);

        GameObject terrainGO = Terrain.CreateTerrainGameObject(terrainData);
        terrainGO.name = terrainName;
        // Start Position stays (0,0,0) in X/Z, matching Terrain Toolbox's own
        // default - only Y moves, to place the terrain at true elevation.
        terrainGO.transform.position = new Vector3(0f, elevationMin, 0f);

        return terrainGO;
    }

    // ------------------------------------------------------------------
    // Minimal PNG decoder for exactly one case: single-channel (grayscale),
    // 16-bit-per-sample, non-interlaced PNG - which is exactly what PIL's
    // "I;16" save mode produces, and exactly what our heightmaps are.
    // Deliberately does not attempt to handle any other PNG variant; it
    // throws a clear error instead of silently mis-decoding one.
    // ------------------------------------------------------------------
    public static ushort[,] LoadGrayscale16Png(string path, out int width, out int height)
    {
        byte[] data = File.ReadAllBytes(path);

        byte[] pngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        for (int i = 0; i < 8; i++)
            if (data[i] != pngSignature[i])
                throw new Exception("not a PNG file (bad signature)");

        int pos = 8;
        width = 0;
        height = 0;
        int bitDepth = -1, colorType = -1, interlace = -1;
        var idat = new MemoryStream();

        while (pos + 8 <= data.Length)
        {
            int length = ReadInt32BE(data, pos); pos += 4;
            string type = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            int chunkStart = pos;

            if (type == "IHDR")
            {
                width = ReadInt32BE(data, chunkStart);
                height = ReadInt32BE(data, chunkStart + 4);
                bitDepth = data[chunkStart + 8];
                colorType = data[chunkStart + 9];
                interlace = data[chunkStart + 12];
            }
            else if (type == "IDAT")
            {
                idat.Write(data, chunkStart, length);
            }
            else if (type == "IEND")
            {
                break;
            }

            pos = chunkStart + length + 4; // skip data + 4-byte CRC
        }

        if (width <= 0 || height <= 0)
            throw new Exception("missing or invalid IHDR chunk");
        if (bitDepth != 16 || colorType != 0)
            throw new Exception($"expected single-channel 16-bit grayscale (bitDepth=16, colorType=0), got bitDepth={bitDepth} colorType={colorType}");
        if (interlace != 0)
            throw new Exception("interlaced PNGs are not supported by this loader");

        byte[] compressed = idat.ToArray();
        if (compressed.Length < 6)
            throw new Exception("IDAT data too short");

        // zlib stream = 2-byte header + raw deflate + 4-byte Adler32 trailer.
        byte[] raw;
        using (var ms = new MemoryStream(compressed, 2, compressed.Length - 2))
        using (var deflate = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            deflate.CopyTo(outMs);
            raw = outMs.ToArray();
        }

        const int bpp = 2; // 1 channel x 16 bits
        int stride = width * bpp;
        int expected = (stride + 1) * height; // +1 per row for the filter-type byte
        if (raw.Length < expected)
            throw new Exception($"decompressed data too short: got {raw.Length} bytes, expected {expected}");

        byte[] prevLine = new byte[stride]; // all zero for row 0, per spec
        var result = new ushort[height, width];
        int rawPos = 0;

        for (int y = 0; y < height; y++)
        {
            byte filterType = raw[rawPos]; rawPos++;
            byte[] line = new byte[stride];
            Array.Copy(raw, rawPos, line, 0, stride);
            rawPos += stride;

            UnfilterScanline(filterType, line, prevLine, bpp);

            for (int x = 0; x < width; x++)
            {
                int idx = x * 2;
                // PNG stores multi-byte samples big-endian.
                result[y, x] = (ushort)((line[idx] << 8) | line[idx + 1]);
            }

            prevLine = line;
        }

        return result;
    }

    static void UnfilterScanline(byte filterType, byte[] line, byte[] prevLine, int bpp)
    {
        int len = line.Length;
        for (int i = 0; i < len; i++)
        {
            int a = i >= bpp ? line[i - bpp] : 0;
            int b = prevLine[i];
            int c = i >= bpp ? prevLine[i - bpp] : 0;
            int recon;

            switch (filterType)
            {
                case 0: recon = line[i]; break;
                case 1: recon = line[i] + a; break;
                case 2: recon = line[i] + b; break;
                case 3: recon = line[i] + ((a + b) / 2); break;
                case 4: recon = line[i] + PaethPredictor(a, b, c); break;
                default: throw new Exception($"unknown PNG filter type {filterType}");
            }

            line[i] = (byte)(recon & 0xFF);
        }
    }

    static int PaethPredictor(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    // Big-endian 32-bit read, used for PNG chunk lengths and IHDR fields.
    static int ReadInt32BE(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
