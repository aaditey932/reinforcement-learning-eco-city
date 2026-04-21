using UnityEngine;

namespace EcoCity
{
    /// <summary>
    /// Quantile cutoffs used to classify each cell's normalised elevation
    /// into a <see cref="BiomeBand"/>. All four values are in [0, 1] and must
    /// be strictly increasing. The default (0.15 / 0.40 / 0.70 / 0.90) gives
    /// a balanced mix; push <see cref="water"/> up to flood more cells, push
    /// <see cref="upland"/> down to carve more mountains.
    /// </summary>
    [System.Serializable]
    public struct BiomeThresholds
    {
        [Range(0f, 1f)] public float water;    // below -> Water
        [Range(0f, 1f)] public float lowland;  // [water, lowland) -> Lowland
        [Range(0f, 1f)] public float midland;  // [lowland, midland) -> Midland
        [Range(0f, 1f)] public float upland;   // [midland, upland) -> Upland, above -> Peak

        public static BiomeThresholds Default => new BiomeThresholds
        {
            water = 0.15f,
            lowland = 0.40f,
            midland = 0.70f,
            upland = 0.90f,
        };

        /// <summary>Clamp + re-order thresholds so the bands never overlap.</summary>
        public BiomeThresholds Sanitized()
        {
            float w = Mathf.Clamp01(water);
            float l = Mathf.Max(w + 1e-3f, Mathf.Clamp01(lowland));
            float m = Mathf.Max(l + 1e-3f, Mathf.Clamp01(midland));
            float u = Mathf.Max(m + 1e-3f, Mathf.Clamp01(upland));
            return new BiomeThresholds { water = w, lowland = l, midland = m, upland = Mathf.Min(0.999f, u) };
        }
    }

    /// <summary>
    /// Maps the 2D city grid onto the 3D low-poly terrain.
    ///
    /// Given a <see cref="TerrainGenerator"/> (whose mesh is stored on its
    /// transform's <see cref="MeshCollider"/>), this samples terrain height
    /// at each cell's world-space center by raycasting straight down onto
    /// the collider. Heights are then classified into five biome bands by
    /// quantile so the classification is stable even when the terrain's
    /// overall altitude range changes between runs.
    /// </summary>
    public class BiomeSampler
    {
        /// <summary>Approximate terrain bounds in XZ used for tile placement.</summary>
        public Vector3 origin;
        public float cellWorldSize;

        /// <summary>Terrain height at each grid cell's center (world Y).</summary>
        public float[,] heights;

        /// <summary>Classified biome band per cell.</summary>
        public BiomeBand[,] bands;

        public int gridSize;

        public static BiomeSampler Sample(TerrainGenerator terrain, int gridSize)
        {
            return Sample(terrain, gridSize, BiomeThresholds.Default);
        }

        public static BiomeSampler Sample(TerrainGenerator terrain, int gridSize, BiomeThresholds thresholds)
        {
            if (terrain == null)
                throw new System.ArgumentNullException(nameof(terrain));

            thresholds = thresholds.Sanitized();
            var sampler = new BiomeSampler { gridSize = gridSize };

            Transform t = terrain.transform;
            float sizeX = terrain.sizeX;
            float sizeZ = terrain.sizeY; // TerrainGenerator uses XY for the plane but the mesh lays out XZ.

            sampler.cellWorldSize = Mathf.Min(sizeX, sizeZ) / gridSize;
            sampler.origin = new Vector3(
                t.position.x + (sizeX - sampler.cellWorldSize * gridSize) * 0.5f,
                0f,
                t.position.z + (sizeZ - sampler.cellWorldSize * gridSize) * 0.5f);

            sampler.heights = new float[gridSize, gridSize];
            sampler.bands = new BiomeBand[gridSize, gridSize];

            var collider = t.GetComponent<MeshCollider>();
            float minH = float.PositiveInfinity;
            float maxH = float.NegativeInfinity;

            float rayHeight = 10000f;

            for (int r = 0; r < gridSize; r++)
            {
                for (int c = 0; c < gridSize; c++)
                {
                    Vector3 center = sampler.CellCenter(r, c);
                    float y = t.position.y;

                    if (collider != null)
                    {
                        var origin = new Vector3(center.x, t.position.y + rayHeight, center.z);
                        if (collider.Raycast(new Ray(origin, Vector3.down), out var hit, rayHeight * 2f))
                        {
                            y = hit.point.y;
                        }
                    }

                    sampler.heights[r, c] = y;
                    if (y < minH) minH = y;
                    if (y > maxH) maxH = y;
                }
            }

            // Quantile-based banding keeps each band roughly populated even
            // when the perlin output is skewed. Fall back to linear bins if
            // the terrain is essentially flat.
            float span = maxH - minH;
            if (span < 1e-3f)
            {
                for (int r = 0; r < gridSize; r++)
                for (int c = 0; c < gridSize; c++)
                    sampler.bands[r, c] = BiomeBand.Midland;
                return sampler;
            }

            for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
            {
                float norm = (sampler.heights[r, c] - minH) / span;
                BiomeBand band;
                if      (norm < thresholds.water)   band = BiomeBand.Water;
                else if (norm < thresholds.lowland) band = BiomeBand.Lowland;
                else if (norm < thresholds.midland) band = BiomeBand.Midland;
                else if (norm < thresholds.upland)  band = BiomeBand.Upland;
                else                                band = BiomeBand.Peak;
                sampler.bands[r, c] = band;
            }

            return sampler;
        }

        public Vector3 CellCenter(int row, int col)
        {
            return new Vector3(
                origin.x + (col + 0.5f) * cellWorldSize,
                0f,
                origin.z + (row + 0.5f) * cellWorldSize);
        }
    }
}
