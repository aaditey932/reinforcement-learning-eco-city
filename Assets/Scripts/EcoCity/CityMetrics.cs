using UnityEngine;

namespace EcoCity
{
    /// <summary>
    /// Tunable weights for the multi-objective reward. Exposed on the
    /// environment component so Experiment 2 (reward sensitivity) can sweep
    /// over sustainability vs growth trade-offs.
    /// </summary>
    [System.Serializable]
    public class RewardWeights
    {
        [Tooltip("Livability weight (population satisfaction).")]
        public float alpha = 1.0f;
        [Tooltip("Pollution penalty weight.")]
        public float beta = 1.2f;
        [Tooltip("Traffic penalty weight.")]
        public float gamma = 0.7f;
        [Tooltip("Energy mismatch penalty weight.")]
        public float delta = 0.8f;
    }

    /// <summary>
    /// Snapshot of raw (normalized) city metrics for one grid state. All
    /// values are in [0, 1] except the signed reward delta.
    /// </summary>
    public struct CityMetricsSnapshot
    {
        public float livability;
        public float pollution;
        public float traffic;
        public float energySupply;
        public float energyDemand;
        public float energyMismatch;
    }

    /// <summary>
    /// Pure-function scorer for the city grid. Kept free of Unity references
    /// so it can be called from baselines, training, and editor tooling alike.
    /// </summary>
    public static class CityMetrics
    {
        // Per-zone demand / supply coefficients. Deliberately small integers
        // so the metrics stay in a predictable range after normalisation by
        // the total cell count.
        private const float ResidentialDemand = 1.0f;
        private const float CommercialDemand = 0.6f;
        private const float IndustrialDemand = 1.2f;
        private const float EnergySupplyPerCell = 2.5f;

        private const float IndustrialPollution = 1.0f;
        private const float GreenOffset = 0.7f;

        private const float RoadLocalRelief = 0.5f;
        private const int RoadReliefRadius = 1;

        private const float PollutionDecayRadius = 2f;

        public static CityMetricsSnapshot Compute(ZoneType[,] grid, BiomeBand[,] biome)
        {
            int n = grid.GetLength(0);
            float totalCells = Mathf.Max(1, n * n);

            int residential = 0, commercial = 0, industrial = 0;
            int green = 0, road = 0, energy = 0;

            for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                switch (grid[r, c])
                {
                    case ZoneType.Residential: residential++; break;
                    case ZoneType.Commercial:  commercial++;  break;
                    case ZoneType.Industrial:  industrial++;  break;
                    case ZoneType.Green:       green++;       break;
                    case ZoneType.Road:        road++;        break;
                    case ZoneType.Energy:      energy++;      break;
                }
            }

            // Energy: demand side comes from inhabited / productive zones;
            // supply from Energy cells. Mismatch in either direction hurts.
            float energyDemand = residential * ResidentialDemand
                               + commercial  * CommercialDemand
                               + industrial  * IndustrialDemand;
            float energySupply = energy * EnergySupplyPerCell;
            float energyScale = Mathf.Max(1f, totalCells * ResidentialDemand);
            float energyMismatchNorm = Mathf.Clamp01(
                Mathf.Abs(energyDemand - energySupply) / energyScale);
            float energyServedFrac = energyDemand > 0f
                ? Mathf.Clamp01(energySupply / energyDemand)
                : 1f;

            // Pollution: sum of industrial emissions with distance-decay to
            // residential cells, minus green offset. Normalised per residential
            // receiver so the scale is roughly comparable across grids.
            float pollutionAccum = 0f;
            if (industrial > 0)
            {
                for (int rr = 0; rr < n; rr++)
                for (int rc = 0; rc < n; rc++)
                {
                    if (grid[rr, rc] != ZoneType.Industrial) continue;
                    for (int hr = 0; hr < n; hr++)
                    for (int hc = 0; hc < n; hc++)
                    {
                        if (grid[hr, hc] != ZoneType.Residential) continue;
                        float d = Mathf.Sqrt((rr - hr) * (rr - hr) + (rc - hc) * (rc - hc));
                        float atten = 1f / (1f + (d / PollutionDecayRadius) * (d / PollutionDecayRadius));
                        pollutionAccum += IndustrialPollution * atten;
                    }
                }
            }
            pollutionAccum -= GreenOffset * green;
            pollutionAccum = Mathf.Max(0f, pollutionAccum);
            float pollutionNorm = Mathf.Clamp01(pollutionAccum / totalCells);

            // Traffic: residential + commercial + industrial produce flow.
            // Road cells inside a small radius reduce local congestion.
            float trafficAccum = 0f;
            for (int rr = 0; rr < n; rr++)
            for (int rc = 0; rc < n; rc++)
            {
                float cellDemand;
                switch (grid[rr, rc])
                {
                    case ZoneType.Residential: cellDemand = 0.8f; break;
                    case ZoneType.Commercial:  cellDemand = 1.0f; break;
                    case ZoneType.Industrial:  cellDemand = 0.9f; break;
                    default: continue;
                }

                float relief = 0f;
                for (int dr = -RoadReliefRadius; dr <= RoadReliefRadius; dr++)
                for (int dc = -RoadReliefRadius; dc <= RoadReliefRadius; dc++)
                {
                    int r2 = rr + dr, c2 = rc + dc;
                    if (r2 < 0 || r2 >= n || c2 < 0 || c2 >= n) continue;
                    if (grid[r2, c2] == ZoneType.Road) relief += RoadLocalRelief;
                }
                trafficAccum += cellDemand / (1f + relief);
            }
            float trafficNorm = Mathf.Clamp01(trafficAccum / totalCells);

            // Livability: rewards residential populations with access to jobs,
            // greenspace, and energy. All expressed as fractional coverages so
            // that over-building any one zone doesn't dominate.
            float residentFrac = residential / totalCells;
            float jobsFrac = Mathf.Clamp01((commercial + industrial) / Mathf.Max(1f, residential));
            float greenAccess = Mathf.Clamp01(green / Mathf.Max(1f, residential * 0.5f));
            float livability = Mathf.Clamp01(
                residentFrac
                * Mathf.Lerp(0.2f, 1f, jobsFrac)
                * Mathf.Lerp(0.3f, 1f, greenAccess)
                * energyServedFrac);

            return new CityMetricsSnapshot
            {
                livability = livability,
                pollution = pollutionNorm,
                traffic = trafficNorm,
                energySupply = Mathf.Clamp01(energySupply / (totalCells * EnergySupplyPerCell)),
                energyDemand = Mathf.Clamp01(energyDemand / energyScale),
                energyMismatch = energyMismatchNorm,
            };
        }

        /// <summary>
        /// Instantaneous weighted reward for a snapshot. Used both as the
        /// per-step signal (via delta-shaping) and as an absolute score.
        /// </summary>
        public static float WeightedScore(CityMetricsSnapshot s, RewardWeights w)
        {
            return w.alpha * s.livability
                 - w.beta  * s.pollution
                 - w.gamma * s.traffic
                 - w.delta * s.energyMismatch;
        }

        /// <summary>
        /// Per-step reward as delta of weighted score between two snapshots.
        /// This keeps the reward centered around zero and unaffected by how
        /// long an episode has been running when zones are sparse.
        /// </summary>
        public static float StepReward(CityMetricsSnapshot prev, CityMetricsSnapshot next, RewardWeights w)
        {
            return WeightedScore(next, w) - WeightedScore(prev, w);
        }
    }
}
