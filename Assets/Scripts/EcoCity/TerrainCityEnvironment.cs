using System;
using UnityEngine;

namespace EcoCity
{
    /// <summary>
    /// Core Eco-City environment. Holds the grid, applies discrete zone-
    /// placement actions, computes the weighted reward, and exposes an
    /// observation vector for the ML-Agents policy. The component is a
    /// stand-alone MonoBehaviour so it can also be driven by baseline
    /// policies or editor tooling without any Agent in the loop.
    ///
    /// Action space (single discrete branch of size gridSize * gridSize * 6):
    ///     action = cellIndex * 6 + placeableZoneIndex
    ///     cellIndex = row * gridSize + col
    ///     placeableZoneIndex in [0, 6) maps 1:1 to ZoneType.Residential..Energy
    /// </summary>
    public class TerrainCityEnvironment : MonoBehaviour
    {
        [Header("Grid / episode")]
        [Range(4, 20)] public int gridSize = 12;
        [Range(50, 2000)] public int maxSteps = 400;

        [Header("Terrain")]
        [Tooltip("Source of terrain heights used to classify biome bands. If left null the component searches the scene for one at Awake.")]
        public TerrainGenerator terrain;
        [Tooltip("Quantile cutoffs used to classify elevation into biome bands. Driven by TerrainTuner if one is present on the terrain.")]
        public BiomeThresholds biomeThresholds = BiomeThresholds.Default;

        [Header("Reward")]
        public RewardWeights weights = new RewardWeights();

        [Header("Experiment 3 (generalization) knobs")]
        [Tooltip("Scales all zone-derived demand (energy / traffic).")]
        [Range(0.5f, 2.0f)] public float demandMultiplier = 1f;
        [Tooltip("Scales the pollution penalty weight at reset. Multiplied into beta.")]
        [Range(0.5f, 3.0f)] public float pollutionPenaltyMultiplier = 1f;

        [Header("Per-episode randomisation")]
        [Tooltip("If on, every episode starts by re-seeding the TerrainTuner and rebuilding the terrain so the policy sees a fresh world. Works best when a TerrainTuner is attached to the terrain.")]
        public bool randomTerrainEachEpisode = true;
        [Tooltip("If non-zero, the first episode uses this seed so runs are reproducible. Set to 0 for a random starting seed.")]
        public int initialTerrainSeed = 0;

        public event Action<ZoneType[,]> OnGridChanged;
        public event Action<CityMetricsSnapshot> OnMetricsUpdated;
        /// <summary>Fired after the terrain mesh and biome sampler are rebuilt. Listeners (e.g. the visualizer) should refresh any per-cell positions.</summary>
        public event Action OnTerrainRegenerated;

        /// <summary>Seed currently in use for the terrain (forwarded to TerrainTuner). Read-only.</summary>
        public int CurrentTerrainSeed { get; private set; }

        public ZoneType[,] Grid => m_Grid;
        public BiomeBand[,] Biome => m_Sampler != null ? m_Sampler.bands : null;
        public BiomeSampler Sampler => m_Sampler;
        public int CurrentStep { get; private set; }
        public CityMetricsSnapshot LastMetrics { get; private set; }

        public int ObservationSize
        {
            get
            {
                int zoneOneHot = gridSize * gridSize * CityMetricsUtility.NumZoneTypesWithEmpty;
                int globals = 4;
                int zoneRatios = CityMetricsUtility.NumPlaceableZones;
                int biomePlane = gridSize * gridSize;
                return zoneOneHot + globals + zoneRatios + biomePlane;
            }
        }

        public int ActionSpaceSize => gridSize * gridSize * CityMetricsUtility.NumPlaceableZones;

        private ZoneType[,] m_Grid;
        private BiomeSampler m_Sampler;
        private CityMetricsSnapshot m_PrevMetrics;
        private float[] m_ObsBuffer;
        private bool m_Initialized;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (m_Initialized) return;
            if (terrain == null) terrain = FindObjectOfType<TerrainGenerator>();
            if (terrain != null && terrain.GetComponent<MeshFilter>() != null
                && terrain.GetComponent<MeshFilter>().sharedMesh == null)
            {
                // TerrainGenerator builds its mesh in Initiate() triggered by
                // the custom inspector. Make sure we have geometry to raycast.
                terrain.Initiate();
            }

            m_Grid = new ZoneType[gridSize, gridSize];
            m_Sampler = terrain != null
                ? BiomeSampler.Sample(terrain, gridSize, biomeThresholds)
                : BuildFallbackSampler();
            m_ObsBuffer = new float[ObservationSize];
            m_PrevMetrics = CityMetrics.Compute(m_Grid, m_Sampler.bands);
            LastMetrics = m_PrevMetrics;
            CurrentStep = 0;
            m_Initialized = true;
        }

        private BiomeSampler BuildFallbackSampler()
        {
            // When there's no terrain wired up we still want a valid sampler
            // (useful for unit-style runs). Default everything to Midland.
            var s = new BiomeSampler
            {
                origin = Vector3.zero,
                cellWorldSize = 10f,
                gridSize = gridSize,
            };
            s.heights = new float[gridSize, gridSize];
            s.bands = new BiomeBand[gridSize, gridSize];
            for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
                s.bands[r, c] = BiomeBand.Midland;
            return s;
        }

        public void ResetEnvironment()
        {
            EnsureInitialized();

            if (randomTerrainEachEpisode)
                RegenerateTerrainWithNewSeed();

            for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
                m_Grid[r, c] = ZoneType.Empty;

            CurrentStep = 0;
            m_PrevMetrics = CityMetrics.Compute(m_Grid, m_Sampler.bands);
            LastMetrics = m_PrevMetrics;

            OnGridChanged?.Invoke(m_Grid);
            OnMetricsUpdated?.Invoke(LastMetrics);
        }

        /// <summary>
        /// Picks a fresh random seed, pushes it into the TerrainTuner (if
        /// attached), rebuilds the low-poly mesh, and re-classifies every
        /// cell's biome band. Fires <see cref="OnTerrainRegenerated"/> when
        /// done so the visualizer can re-snap its tiles to the new heights.
        /// Falls back to a no-op if no TerrainTuner / TerrainGenerator is
        /// available.
        /// </summary>
        public void RegenerateTerrainWithNewSeed()
        {
            if (terrain == null) return;

            int seed;
            if (CurrentTerrainSeed == 0 && initialTerrainSeed != 0)
            {
                seed = initialTerrainSeed;
            }
            else
            {
                // Use Unity's Random with an unseeded slice so the sequence
                // advances even if the tuner internally re-seeds it.
                seed = UnityEngine.Random.Range(1, int.MaxValue);
            }
            CurrentTerrainSeed = seed;

            var tuner = terrain.GetComponent<TerrainTuner>();
            if (tuner != null)
            {
                tuner.seed = seed;
                tuner.Apply(); // rebuilds the mesh with the new seed
            }
            else
            {
                // No tuner: just poke the generator's seed and rebuild directly.
                terrain.seed = seed;
                var prevState = UnityEngine.Random.state;
                UnityEngine.Random.InitState(seed);
                try { terrain.Initiate(); }
                finally { UnityEngine.Random.state = prevState; }
            }

            m_Sampler = BiomeSampler.Sample(terrain, gridSize, biomeThresholds);
            OnTerrainRegenerated?.Invoke();
        }

        public CityStepResult Step(int action)
        {
            EnsureInitialized();
            CurrentStep++;

            DecodeAction(action, out int row, out int col, out ZoneType zone);

            bool valid = IsActionValidInternal(row, col, zone);
            if (valid)
            {
                m_Grid[row, col] = zone;
            }

            var next = CityMetrics.Compute(m_Grid, m_Sampler.bands);

            // Scale the penalty weight via the Experiment-3 multiplier without
            // mutating the serialized weights object.
            var effective = new RewardWeights
            {
                alpha = weights.alpha,
                beta = weights.beta * pollutionPenaltyMultiplier,
                gamma = weights.gamma,
                delta = weights.delta * demandMultiplier,
            };

            float reward = CityMetrics.StepReward(m_PrevMetrics, next, effective);
            if (!valid) reward -= 0.02f; // Tiny shove away from invalid picks.

            m_PrevMetrics = next;
            LastMetrics = next;

            if (valid)
                OnGridChanged?.Invoke(m_Grid);
            OnMetricsUpdated?.Invoke(next);

            return new CityStepResult
            {
                reward = reward,
                terminated = CurrentStep >= maxSteps,
            };
        }

        public void DecodeAction(int action, out int row, out int col, out ZoneType zone)
        {
            int zoneCount = CityMetricsUtility.NumPlaceableZones;
            int cellIndex = action / zoneCount;
            int zoneIndex = action % zoneCount;
            row = cellIndex / gridSize;
            col = cellIndex % gridSize;
            zone = CityMetricsUtility.PlaceableZoneFromIndex(zoneIndex);
        }

        public int EncodeAction(int row, int col, ZoneType zone)
        {
            int zoneCount = CityMetricsUtility.NumPlaceableZones;
            int cellIndex = row * gridSize + col;
            int zoneIndex = CityMetricsUtility.PlaceableZoneToIndex(zone);
            return cellIndex * zoneCount + zoneIndex;
        }

        public bool IsZoneAllowedAt(int row, int col, ZoneType zone)
        {
            if (row < 0 || row >= gridSize || col < 0 || col >= gridSize) return false;
            if (zone == ZoneType.Empty) return false;
            var band = m_Sampler.bands[row, col];
            return CityMetricsUtility.IsZoneAllowedInBiome(zone, band);
        }

        /// <summary>
        /// Convenience overload so the agent's mask can call this with the raw
        /// placeable zone index produced by action-decoding.
        /// </summary>
        public bool IsZoneAllowedAt(int row, int col, int placeableZoneIndex)
        {
            var zone = CityMetricsUtility.PlaceableZoneFromIndex(placeableZoneIndex);
            return IsZoneAllowedAt(row, col, zone);
        }

        public bool IsActionValid(int action)
        {
            if (action < 0 || action >= ActionSpaceSize) return false;
            DecodeAction(action, out int r, out int c, out ZoneType z);
            return IsActionValidInternal(r, c, z);
        }

        private bool IsActionValidInternal(int row, int col, ZoneType zone)
        {
            if (!IsZoneAllowedAt(row, col, zone)) return false;
            // Disallow re-placing the same zone on a cell that already has it
            // (would be a wasted step). Allow overwriting with a different zone
            // so the agent can course-correct.
            return m_Grid[row, col] != zone;
        }

        public float[] GetObservation()
        {
            EnsureInitialized();
            if (m_ObsBuffer == null || m_ObsBuffer.Length != ObservationSize)
                m_ObsBuffer = new float[ObservationSize];

            int idx = 0;
            int nz = CityMetricsUtility.NumZoneTypesWithEmpty;

            for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
            {
                int z = (int)m_Grid[r, c];
                for (int k = 0; k < nz; k++)
                    m_ObsBuffer[idx++] = (k == z) ? 1f : 0f;
            }

            m_ObsBuffer[idx++] = LastMetrics.livability;
            m_ObsBuffer[idx++] = LastMetrics.pollution;
            m_ObsBuffer[idx++] = LastMetrics.traffic;
            m_ObsBuffer[idx++] = LastMetrics.energyMismatch;

            int totalCells = gridSize * gridSize;
            int[] counts = new int[CityMetricsUtility.NumPlaceableZones];
            for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
            {
                var z = m_Grid[r, c];
                if (z == ZoneType.Empty) continue;
                counts[CityMetricsUtility.PlaceableZoneToIndex(z)]++;
            }
            for (int k = 0; k < counts.Length; k++)
                m_ObsBuffer[idx++] = counts[k] / (float)totalCells;

            float bandDenom = Mathf.Max(1, CityMetricsUtility.NumBiomeBands - 1);
            for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
                m_ObsBuffer[idx++] = (int)m_Sampler.bands[r, c] / bandDenom;

            return m_ObsBuffer;
        }
    }
}
