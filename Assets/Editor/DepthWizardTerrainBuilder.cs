// DepthWizardTerrainBuilder.cs
//
// Builds a Unity Terrain directly from a 16-bit grayscale heightmap PNG,
// using only public Terrain/TerrainData APIs - no dependency on the Terrain
// Toolbox window itself, so it runs from one menu click with no dialogs.
//
// Why this exists instead of using Terrain Toolbox's GUI: the numbers below
// (world size, elevation range) come straight from Team A's manifest.json,
// so there is no scaling math to get wrong by hand.
//
// IMPORTANT: run "DepthWizard > Build Pyramid Test Terrain" FIRST and check
// the Console log it prints. A correct run reports the corner at ~0m and the
// center at ~1000m. If those numbers are off, do not trust the real-data run.
//
// NOTE ON HEIGHTMAP LOADING: this deliberately does NOT go through Unity's
// AssetImporter/TextureImporter/AssetDatabase pipeline at all. That route
// (import as a Texture2D asset, flip isReadable, force an R16 platform
// override, SaveAndReimport, then GetPixels) turned out to be fragile in
// practice - a texture that failed to import cleanly once can get stuck in
// a state where GetPixels keeps throwing "not readable" even after
// isReadable is set, and even a "successful" reimport risks Unity quietly
// re-quantizing a 16-bit PNG down to 8 bits per channel, which would throw
// away most of the elevation detail without any error at all.
//
// Instead this reads the PNG file's bytes straight off disk and decodes the
// 16-bit grayscale pixel data itself (see LoadGrayscale16Png below). That
// sidesteps AssetDatabase entirely - there is no importer to misconfigure
// and no readable flag to fight - and it guarantees exact 16-bit precision
// because nothing ever downsamples through an 8-bit-per-channel Texture2D.
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;

public static class DepthWizardTerrainBuilder
{
    // Unity terrain heightmap resolution must be 2^n + 1 (33, 65, ... 4097).
    static readonly int[] ValidHeightmapResolutions = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };

    // Auto-run the pyramid test the moment this script finishes compiling -
    // no menu click needed. Guarded so it only builds once; delete the
    // PyramidTest_Terrain GameObject from the scene if you want it to redo.
    [DidReloadScripts]
    static void AutoRunOnce()
    {
        bool pyramidExists = GameObject.Find("PyramidTest_Terrain") != null;
        if (!pyramidExists)
        {
            string fullPath = Path.Combine(Application.dataPath, "..", "Assets/Terrain/PyramidTest/pyramid_heightmap.png");
            if (File.Exists(fullPath))
            {
                Debug.Log("DepthWizard: auto-running pyramid test now that the script compiled...");
                BuildPyramidTest();
                pyramidExists = true;
            }
        }

        // Also auto-run the real-data build if that heightmap is present too -
        // MenuItem clicks have proven unreliable to drive by remote GUI
        // automation, so do both from the one recompile.
        bool patch43Exists = GameObject.Find("Patch43_Terrain") != null;
        if (!patch43Exists)
        {
            string patch43Path = Path.Combine(Application.dataPath, "..", "Assets/Terrain/Heightmaps/patch_43_heightmap.png");
            if (File.Exists(patch43Path))
            {
                Debug.Log("DepthWizard: auto-running patch_43 real-data build too...");
                BuildPatch43();
                patch43Exists = true;
            }
        }

        // IMPORTANT: creating GameObjects from an editor script like this
        // does NOT mark the scene dirty the way a manual edit would - so
        // File > Save (and Ctrl+S) can silently no-op even with the new
        // terrain sitting right there in the Hierarchy, leaving it out of
        // the saved .unity file entirely. Force the save explicitly instead
        // of trusting Unity's dirty-tracking.
        if (pyramidExists || patch43Exists)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"DepthWizard: force-saved scene '{scene.name}' to disk (script-created GameObjects don't auto-dirty the scene).");
        }
    }

    [MenuItem("DepthWizard/Build Pyramid Test Terrain")]
    public static void BuildPyramidTest()
    {
        BuildTerrainFromHeightmap(
            heightmapAssetPath: "Assets/Terrain/PyramidTest/pyramid_heightmap.png",
            worldWidth: 1000f,
            worldLength: 1000f,
            elevationMin: 0f,
            elevationMax: 1000f,
            terrainName: "PyramidTest_Terrain",
            isKnownShapeTest: true
        );
    }

    [MenuItem("DepthWizard/Build Patch 43 Terrain (real data)")]
    public static void BuildPatch43()
    {
        BuildTerrainFromHeightmap(
            heightmapAssetPath: "Assets/Terrain/Heightmaps/patch_43_heightmap.png",
            worldWidth: 5120.43f,
            worldLength: 5133.73f,
            elevationMin: 379.0f,
            elevationMax: 718.54f,
            terrainName: "Patch43_Terrain",
            isKnownShapeTest: false
        );
    }

    static void BuildTerrainFromHeightmap(
        string heightmapAssetPath, float worldWidth, float worldLength,
        float elevationMin, float elevationMax, string terrainName, bool isKnownShapeTest)
    {
        string fullPath = Path.Combine(Application.dataPath, "..", heightmapAssetPath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"DepthWizard: no file at '{fullPath}'. " +
                            "Check the path matches where the file actually is in Assets/Terrain.");
            return;
        }

        ushort[,] raw16;
        int srcW, srcH;
        try
        {
            raw16 = LoadGrayscale16Png(fullPath, out srcW, out srcH);
        }
        catch (Exception e)
        {
            Debug.LogError($"DepthWizard: failed to decode 16-bit grayscale PNG at '{heightmapAssetPath}': {e.Message}");
            return;
        }

        int srcMax = Mathf.Max(srcW, srcH);
        int resolution = PickHeightmapResolution(srcMax);

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

        var terrainData = new TerrainData();
        terrainData.heightmapResolution = resolution;
        float elevationSpan = elevationMax - elevationMin;
        terrainData.size = new Vector3(worldWidth, elevationSpan, worldLength);
        terrainData.SetHeights(0, 0, heights);

        const string dataDir = "Assets/Terrain/Generated";
        if (!AssetDatabase.IsValidFolder(dataDir))
            AssetDatabase.CreateFolder("Assets/Terrain", "Generated");
        string dataAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{dataDir}/{terrainName}Data.asset");
        AssetDatabase.CreateAsset(terrainData, dataAssetPath);

        GameObject existing = GameObject.Find(terrainName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

        GameObject terrainGO = Terrain.CreateTerrainGameObject(terrainData);
        terrainGO.name = terrainName;
        // Start Position stays (0,0,0) in X/Z, matching Terrain Toolbox's own
        // default - only Y moves, to place the terrain at true elevation.
        terrainGO.transform.position = new Vector3(0f, elevationMin, 0f);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = terrainGO;
        EditorGUIUtility.PingObject(terrainGO);

        Debug.Log($"DepthWizard: built '{terrainName}' from {srcW}x{srcH} heightmap -> " +
                  $"terrain resolution {resolution}. Size {worldWidth}m x {elevationSpan}m x {worldLength}m, " +
                  $"elevation {elevationMin}m to {elevationMax}m.");

        if (isKnownShapeTest)
        {
            int last = resolution - 1;
            int center = resolution / 2;
            float cornerElevation = elevationMin + terrainData.GetHeight(0, 0);
            float farCornerElevation = elevationMin + terrainData.GetHeight(last, last);
            float centerElevation = elevationMin + terrainData.GetHeight(center, center);

            Debug.Log("DepthWizard PYRAMID CHECK - expect corner ~0m, center ~1000m:\n" +
                      $"  corner (0,0):        {cornerElevation:F1} m\n" +
                      $"  corner ({last},{last}): {farCornerElevation:F1} m\n" +
                      $"  center ({center},{center}): {centerElevation:F1} m");

            bool ok = Mathf.Abs(cornerElevation) < 5f
                      && Mathf.Abs(farCornerElevation) < 5f
                      && Mathf.Abs(centerElevation - 1000f) < 5f;

            if (ok)
                Debug.Log("DepthWizard: PASS - pyramid shape and scale are correct. Safe to run the real-data build.");
            else
                Debug.LogError("DepthWizard: FAIL - numbers above don't match the known pyramid. " +
                                "Do not trust the real-data terrain until this is fixed.");
        }
    }

    static int PickHeightmapResolution(int srcMax)
    {
        foreach (var r in ValidHeightmapResolutions)
            if (r >= srcMax) return r;
        return ValidHeightmapResolutions[ValidHeightmapResolutions.Length - 1];
    }

    // ------------------------------------------------------------------
    // Minimal PNG decoder for exactly one case: single-channel (grayscale),
    // 16-bit-per-sample, non-interlaced PNG - which is exactly what PIL's
    // "I;16" save mode produces, and exactly what our heightmaps are.
    // Deliberately does not attempt to handle any other PNG variant; it
    // throws a clear error instead of silently mis-decoding one.
    // ------------------------------------------------------------------
    static ushort[,] LoadGrayscale16Png(string path, out int width, out int height)
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
