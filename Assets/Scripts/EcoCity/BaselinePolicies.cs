using System.Collections.Generic;
using UnityEngine;

namespace EcoCity
{
    /// <summary>
    /// A non-learning policy mapping an environment state to a single discrete
    /// action. Baselines are used as reference points in Experiment 1.
    /// </summary>
    public interface IPolicy
    {
        string Name { get; }
        int SelectAction(TerrainCityEnvironment env);
    }

    /// <summary>Picks any valid action uniformly at random.</summary>
    public class RandomPolicy : IPolicy
    {
        public string Name => "Random";

        public int SelectAction(TerrainCityEnvironment env)
        {
            int n = env.ActionSpaceSize;
            for (int i = 0; i < 256; i++)
            {
                int a = Random.Range(0, n);
                if (env.IsActionValid(a)) return a;
            }
            for (int a = 0; a < n; a++)
                if (env.IsActionValid(a)) return a;
            return 0;
        }
    }

    /// <summary>
    /// Greedy growth policy: always try to place Residential somewhere valid;
    /// if the grid is saturated with Residential, fall back to Commercial,
    /// then Energy, etc. Mirrors "grow the city as fast as possible" behavior.
    /// </summary>
    public class GreedyPopulationPolicy : IPolicy
    {
        public string Name => "GreedyPopulation";

        private static readonly ZoneType[] s_Priority =
        {
            ZoneType.Residential,
            ZoneType.Commercial,
            ZoneType.Energy,
            ZoneType.Road,
            ZoneType.Industrial,
            ZoneType.Green,
        };

        public int SelectAction(TerrainCityEnvironment env)
        {
            int n = env.gridSize;
            foreach (var zone in s_Priority)
            {
                for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (env.Grid[r, c] == zone) continue;
                    if (!env.IsZoneAllowedAt(r, c, zone)) continue;
                    if (env.Grid[r, c] == ZoneType.Empty)
                        return env.EncodeAction(r, c, zone);
                }
            }
            // Everything filled — return any valid action.
            return new RandomPolicy().SelectAction(env);
        }
    }

    /// <summary>
    /// Balanced heuristic: maintain a target share of zones across the grid.
    /// At each step picks the zone most under-represented relative to target
    /// and places it on a valid empty cell, preferring cells near residential
    /// areas so services cluster naturally.
    /// </summary>
    public class BalancedHeuristicPolicy : IPolicy
    {
        public string Name => "BalancedHeuristic";

        // Roughly PRD-inspired target mix. Must sum to ~1.
        private static readonly float[] s_Targets =
        {
            0.30f, // Residential
            0.15f, // Commercial
            0.10f, // Industrial
            0.20f, // Green
            0.15f, // Road
            0.10f, // Energy
        };

        public int SelectAction(TerrainCityEnvironment env)
        {
            int n = env.gridSize;
            int totalCells = n * n;

            var counts = new int[CityMetricsUtility.NumPlaceableZones];
            int filled = 0;
            for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                var z = env.Grid[r, c];
                if (z == ZoneType.Empty) continue;
                counts[CityMetricsUtility.PlaceableZoneToIndex(z)]++;
                filled++;
            }

            // Rank zones by deficit relative to target share.
            var ranking = new List<(ZoneType zone, float deficit)>();
            for (int k = 0; k < counts.Length; k++)
            {
                float share = counts[k] / (float)totalCells;
                float deficit = s_Targets[k] - share;
                ranking.Add((CityMetricsUtility.PlaceableZoneFromIndex(k), deficit));
            }
            ranking.Sort((a, b) => b.deficit.CompareTo(a.deficit));

            foreach (var (zone, _) in ranking)
            {
                int bestR = -1, bestC = -1;
                float bestScore = float.NegativeInfinity;

                for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (env.Grid[r, c] != ZoneType.Empty) continue;
                    if (!env.IsZoneAllowedAt(r, c, zone)) continue;

                    // Prefer cells that reduce distance to existing residentials
                    // (so services cluster around housing). For residential itself
                    // we spread out slightly instead.
                    float score = 0f;
                    float minDist = float.PositiveInfinity;
                    for (int rr = 0; rr < n; rr++)
                    for (int cc = 0; cc < n; cc++)
                    {
                        if (env.Grid[rr, cc] != ZoneType.Residential) continue;
                        float d = Mathf.Sqrt((rr - r) * (rr - r) + (cc - c) * (cc - c));
                        if (d < minDist) minDist = d;
                    }
                    if (!float.IsPositiveInfinity(minDist))
                        score = zone == ZoneType.Residential ? minDist : -minDist;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestR = r; bestC = c;
                    }
                }

                if (bestR >= 0)
                    return env.EncodeAction(bestR, bestC, zone);
            }

            return new RandomPolicy().SelectAction(env);
        }
    }
}
