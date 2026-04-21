using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.Barracuda;

namespace EcoCity
{
    /// <summary>
    /// One-component entry point. Drop this on an empty GameObject in any
    /// scene that already contains a <see cref="TerrainGenerator"/>, press
    /// Play, and the full Eco-City stack is wired up in code. This avoids
    /// shipping any .unity / .prefab YAML in the repo, which is fragile to
    /// hand-edit.
    ///
    /// The bootstrapper creates:
    ///   EcoCity/                         (this GameObject)
    ///     EcoCityEnvironment             (TerrainCityEnvironment + Agent +
    ///                                     BehaviorParameters + DecisionRequester)
    ///     EcoCityVisualizer              (CityVisualizer — transparent tiles)
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class EcoCityBootstrapper : MonoBehaviour
    {
        [Header("Grid")]
        [Range(4, 20)] public int gridSize = 12;
        [Range(50, 2000)] public int maxSteps = 400;

        [Header("Terrain")]
        [Tooltip("If left null the bootstrapper picks the first TerrainGenerator in the scene.")]
        public TerrainGenerator terrain;
        [Tooltip("If enabled, a TerrainTuner is auto-added to the terrain at Play time when one isn't already attached. Select the terrain GameObject to see the sliders.")]
        public bool autoAttachTerrainTuner = true;

        [Header("Reward weights")]
        public RewardWeights weights = new RewardWeights();

        [Header("Per-episode randomisation")]
        [Tooltip("If on, each new episode (train + inference) re-seeds the terrain so the policy sees a fresh world every time. Requires a TerrainTuner on the terrain for best results.")]
        public bool randomTerrainEachEpisode = true;
        [Tooltip("Seed used for the first episode. 0 = pick randomly. Following episodes always pick a new random seed.")]
        public int initialTerrainSeed = 0;

        [Header("ML-Agents")]
        public string behaviorName = "TerrainCityPlanner";
        public BehaviorType behaviorType = BehaviorType.Default;
        [Tooltip("Trained ONNX model (drag the .onnx produced by mlagents-learn here). Leave null for on-line training.")]
        public NNModel onnxModel;
        [Range(1, 20)] public int decisionPeriod = 1;

        [Header("Visualization")]
        [Tooltip("When enabled, coloured transparent tiles are painted on the terrain at every grid change. Disable for headless training for a ~10x speed-up.")]
        public bool spawnVisualizer = true;
        [Range(0f, 1f)] public float tileAlpha = 0.55f;

        [Header("Fast training")]
        [Tooltip("If enabled, the bootstrapper disables the visualizer, runs Unity in low-quality mode, and raises Time.timeScale so PPO collects rollouts much faster. Automatically turned on when the built player is launched with --env-mode=training.")]
        public bool fastTraining = false;
        [Tooltip("Time.timeScale to apply when fastTraining is true. Physics stays stable up to about 20.")]
        [Range(1f, 50f)] public float trainingTimeScale = 20f;
        [Tooltip("Target frame rate during fast training. 0 = uncapped (let Unity run as fast as possible).")]
        [Range(0, 240)] public int trainingTargetFrameRate = 0;

        public TerrainCityEnvironment Environment { get; private set; }
        public TerrainCityMlAgent Agent { get; private set; }
        public CityVisualizer Visualizer { get; private set; }

        /// <summary>True if the player was launched with --env-mode=training (set by the headless build menu).</summary>
        public static bool LaunchedInTrainingMode { get; private set; }

        private void Awake()
        {
#if ECOCITY_TRAINING_BUILD
            // Player was produced by Tools -> Eco-City -> Build Headless
            // Training Player. Force fast-training defaults; CLI args below
            // may still override the time scale.
            LaunchedInTrainingMode = true;
            fastTraining = true;
            spawnVisualizer = false;
#endif
            ApplyCliOverrides();
            ApplyFastTrainingSettings();
            BuildIfNeeded();
        }

        private void ApplyCliOverrides()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--env-mode=training" || a == "--training" || a == "-training")
                {
                    LaunchedInTrainingMode = true;
                    fastTraining = true;
                    spawnVisualizer = false;
                }
                else if (a.StartsWith("--time-scale="))
                {
                    if (float.TryParse(a.Substring("--time-scale=".Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float t))
                    {
                        trainingTimeScale = Mathf.Clamp(t, 1f, 100f);
                    }
                }
            }
        }

        private void ApplyFastTrainingSettings()
        {
            if (!fastTraining) return;

            Time.timeScale = trainingTimeScale;
            Application.runInBackground = true;
            Application.targetFrameRate = trainingTargetFrameRate > 0 ? trainingTargetFrameRate : -1;
            QualitySettings.vSyncCount = 0;

            // Use the lowest quality level to eliminate shadows / AA when the
            // visualizer is still on (e.g. during a monitored training run).
            if (QualitySettings.names != null && QualitySettings.names.Length > 0)
                QualitySettings.SetQualityLevel(0, applyExpensiveChanges: false);

            Debug.Log($"[EcoCity] Fast training mode: timeScale={Time.timeScale}, visualizer={(spawnVisualizer ? "on" : "off")}");
        }

        public void BuildIfNeeded()
        {
            if (Environment != null) return;

            if (terrain == null) terrain = FindObjectOfType<TerrainGenerator>();

            // Ensure a TerrainTuner exists so users can tweak flatness /
            // mountain density / water level / mountain fraction from the
            // Inspector without any manual setup. The tuner rewrites the
            // generator's parameters and rebuilds the mesh BEFORE the biome
            // sampler raycasts the collider, and hands its thresholds to the
            // env so classifications line up with the mesh.
            TerrainTuner tuner = null;
            BiomeThresholds thresholds = BiomeThresholds.Default;
            if (terrain != null)
            {
                tuner = terrain.GetComponent<TerrainTuner>();
                if (tuner == null && autoAttachTerrainTuner)
                {
                    tuner = terrain.gameObject.AddComponent<TerrainTuner>();
                    Debug.Log($"[EcoCity] Auto-attached TerrainTuner to {terrain.name}. " +
                              "Select that GameObject in the Inspector to see the flatness / mountain / water sliders.");
                }
                if (tuner != null)
                {
                    tuner.Apply();
                    thresholds = tuner.ComputeThresholds();
                }
            }

            // Environment + Agent + BehaviorParameters all live on one child
            // GameObject, matching the Crawler example's layout.
            var envGo = new GameObject("EcoCityEnvironment");
            envGo.transform.SetParent(transform, false);

            var env = envGo.AddComponent<TerrainCityEnvironment>();
            env.gridSize = gridSize;
            env.maxSteps = maxSteps;
            env.terrain = terrain;
            env.weights = weights;
            env.biomeThresholds = thresholds;
            env.randomTerrainEachEpisode = randomTerrainEachEpisode;
            env.initialTerrainSeed = initialTerrainSeed;
            env.EnsureInitialized();

            var bp = envGo.AddComponent<BehaviorParameters>();
            bp.BehaviorName = behaviorName;
            bp.BehaviorType = behaviorType;
            bp.BrainParameters.VectorObservationSize = env.ObservationSize;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = Unity.MLAgents.Actuators.ActionSpec
                .MakeDiscrete(env.ActionSpaceSize);
            if (onnxModel != null) bp.Model = onnxModel;

            var agent = envGo.AddComponent<TerrainCityMlAgent>();
            agent.environment = env;

            var dr = envGo.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = Mathf.Max(1, decisionPeriod);
            dr.TakeActionsBetweenDecisions = false;

            Environment = env;
            Agent = agent;

            if (spawnVisualizer)
            {
                var vizGo = new GameObject("EcoCityVisualizer");
                vizGo.transform.SetParent(transform, false);
                var viz = vizGo.AddComponent<CityVisualizer>();
                viz.environment = env;
                viz.terrain = terrain;
                viz.tileAlpha = tileAlpha;
                Visualizer = viz;
            }
        }
    }
}
