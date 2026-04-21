#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EcoCity.EditorTools
{
    /// <summary>
    /// Editor menu items that drive the PRD experiments. All runs are fully
    /// headless on the C# side — no Academy / mlagents-learn connection is
    /// needed. This lets the user ship baseline results without even having
    /// ML-Agents installed.
    ///
    /// Outputs are written to <c>Exports/</c> at the repo root.
    /// </summary>
    public static class ExperimentRunner
    {
        private const int DefaultEpisodes = 50;

        [MenuItem("Tools/Eco-City/Run Baseline Benchmark (50 eps)")]
        public static void RunBaselineBenchmark()
        {
            var env = RequireEnvironment();
            if (env == null) return;

            var policies = new IPolicy[]
            {
                new RandomPolicy(),
                new GreedyPopulationPolicy(),
                new BalancedHeuristicPolicy(),
            };

            var rows = new List<string>
            {
                "policy,episode,total_reward,final_livability,final_pollution,final_traffic,final_energy_mismatch,placed_cells",
            };

            string snapshotDir = Path.Combine("Exports", "snapshots");
            Directory.CreateDirectory(snapshotDir);

            foreach (var policy in policies)
            {
                Debug.Log($"[Eco-City] Benchmarking {policy.Name}...");
                for (int ep = 0; ep < DefaultEpisodes; ep++)
                {
                    env.ResetEnvironment();
                    float total = 0f;
                    int placed = 0;
                    for (int s = 0; s < env.maxSteps; s++)
                    {
                        int a = policy.SelectAction(env);
                        env.DecodeAction(a, out int r, out int c, out ZoneType z);
                        bool wasEmpty = env.Grid[r, c] == ZoneType.Empty;
                        var step = env.Step(a);
                        total += step.reward;
                        if (wasEmpty && env.Grid[r, c] != ZoneType.Empty) placed++;
                        if (step.terminated) break;
                    }

                    var m = env.LastMetrics;
                    rows.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7}",
                        policy.Name, ep, total, m.livability, m.pollution, m.traffic, m.energyMismatch, placed));

                    if (ep == 0)
                    {
                        string snapPath = Path.Combine(snapshotDir,
                            $"{policy.Name}_ep{ep:00}.json");
                        File.WriteAllText(snapPath, SnapshotToJson(env));
                    }
                }
            }

            Directory.CreateDirectory("Exports");
            string outPath = Path.Combine("Exports", "baselines.csv");
            File.WriteAllLines(outPath, rows);
            Debug.Log($"[Eco-City] Baseline benchmark written to {outPath}");
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Eco-City/Sweep Reward Weights (Experiment 2)")]
        public static void SweepRewardWeights()
        {
            var env = RequireEnvironment();
            if (env == null) return;

            float[] alphas = { 0.5f, 1.0f, 1.5f, 2.0f };
            float[] betas  = { 0.5f, 1.2f, 2.0f, 3.0f };

            var policy = new BalancedHeuristicPolicy();

            var rows = new List<string>
            {
                "alpha,beta,episode,total_reward,final_livability,final_pollution,final_traffic,final_energy_mismatch",
            };

            // Preserve original weights so we don't stomp on user settings.
            var original = new RewardWeights
            {
                alpha = env.weights.alpha,
                beta = env.weights.beta,
                gamma = env.weights.gamma,
                delta = env.weights.delta,
            };

            string snapshotDir = Path.Combine("Exports", "snapshots");
            Directory.CreateDirectory(snapshotDir);

            int episodesPerCell = 10;

            foreach (var a in alphas)
            foreach (var b in betas)
            {
                env.weights.alpha = a;
                env.weights.beta = b;
                env.weights.gamma = original.gamma;
                env.weights.delta = original.delta;

                Debug.Log($"[Eco-City] Sweep alpha={a}, beta={b}");
                for (int ep = 0; ep < episodesPerCell; ep++)
                {
                    env.ResetEnvironment();
                    float total = 0f;
                    for (int s = 0; s < env.maxSteps; s++)
                    {
                        var step = env.Step(policy.SelectAction(env));
                        total += step.reward;
                        if (step.terminated) break;
                    }
                    var m = env.LastMetrics;
                    rows.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0:F2},{1:F2},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4}",
                        a, b, ep, total, m.livability, m.pollution, m.traffic, m.energyMismatch));
                }

                string snapPath = Path.Combine(snapshotDir,
                    $"sweep_a{a:F1}_b{b:F1}.json");
                File.WriteAllText(snapPath, SnapshotToJson(env));
            }

            // Restore weights.
            env.weights.alpha = original.alpha;
            env.weights.beta = original.beta;
            env.weights.gamma = original.gamma;
            env.weights.delta = original.delta;

            Directory.CreateDirectory("Exports");
            string outPath = Path.Combine("Exports", "reward_sweep.csv");
            File.WriteAllLines(outPath, rows);
            Debug.Log($"[Eco-City] Reward sweep written to {outPath}");
            AssetDatabase.Refresh();
        }

        private static TerrainCityEnvironment RequireEnvironment()
        {
            var env = Object.FindObjectOfType<TerrainCityEnvironment>();
            if (env == null)
            {
                var boot = Object.FindObjectOfType<EcoCityBootstrapper>();
                if (boot != null)
                {
                    boot.BuildIfNeeded();
                    env = boot.Environment;
                }
            }
            if (env == null)
            {
                EditorUtility.DisplayDialog(
                    "Eco-City not in scene",
                    "Open a scene that has an EcoCityBootstrapper (Tools -> Eco-City -> Spawn Eco-City In Scene).",
                    "OK");
                return null;
            }
            env.EnsureInitialized();
            return env;
        }

        private static string SnapshotToJson(TerrainCityEnvironment env)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"gridSize\": {env.gridSize},\n");
            sb.Append("  \"grid\": [\n");
            int n = env.gridSize;
            for (int r = 0; r < n; r++)
            {
                sb.Append("    [");
                for (int c = 0; c < n; c++)
                {
                    sb.Append((int)env.Grid[r, c]);
                    if (c + 1 < n) sb.Append(", ");
                }
                sb.Append("]");
                if (r + 1 < n) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("  ],\n");
            sb.Append("  \"biome\": [\n");
            for (int r = 0; r < n; r++)
            {
                sb.Append("    [");
                for (int c = 0; c < n; c++)
                {
                    sb.Append((int)env.Biome[r, c]);
                    if (c + 1 < n) sb.Append(", ");
                }
                sb.Append("]");
                if (r + 1 < n) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("  ],\n");
            var m = env.LastMetrics;
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "  \"metrics\": {{\"livability\": {0:F4}, \"pollution\": {1:F4}, \"traffic\": {2:F4}, \"energyMismatch\": {3:F4}}}\n",
                m.livability, m.pollution, m.traffic, m.energyMismatch));
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
#endif
