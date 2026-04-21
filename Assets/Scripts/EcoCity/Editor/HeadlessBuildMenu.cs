#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EcoCity.EditorTools
{
    /// <summary>
    /// Builds a headless standalone player of the currently open scene so it
    /// can be passed to <c>mlagents-learn</c> via <c>--env=Builds/...</c>.
    /// A headless build trains 5-10x faster than play-in-editor because it:
    ///   * skips all rendering and audio,
    ///   * lets <c>mlagents-learn</c> set <c>Time.timeScale</c> very high,
    ///   * doesn't block on vsync,
    ///   * can be replicated with <c>--num-envs=N</c> for parallel rollouts.
    ///
    /// The build's first scene will be whatever is currently open (make sure
    /// the scene already has an <see cref="EcoCityBootstrapper"/>). The build
    /// auto-detects its training mode either from the Scripting Define
    /// <c>ECOCITY_TRAINING_BUILD</c> injected below, or from the command-line
    /// flag <c>--env-mode=training</c> that mlagents-learn forwards.
    /// </summary>
    public static class HeadlessBuildMenu
    {
        private const string BuildDirName = "Builds";

        [MenuItem("Tools/Eco-City/Build Headless Training Player")]
        public static void BuildHeadless()
        {
            var openScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(openScene.path))
            {
                EditorUtility.DisplayDialog("Save the scene first",
                    "Please save the current scene (File -> Save) before building. The built player needs a scene file on disk.",
                    "OK");
                return;
            }

            var boot = Object.FindObjectOfType<EcoCityBootstrapper>();
            if (boot == null)
            {
                EditorUtility.DisplayDialog("No EcoCityBootstrapper",
                    "Add an EcoCityBootstrapper to the scene first (Tools -> Eco-City -> Spawn Eco-City In Scene).",
                    "OK");
                return;
            }

            Directory.CreateDirectory(BuildDirName);

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string ext = GetExtensionFor(target);
            string outPath = Path.Combine(BuildDirName, "EcoCityTraining" + ext);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { openScene.path },
                locationPathName = outPath,
                target = target,
                options = BuildOptions.Development
#if UNITY_2022_2_OR_NEWER
                        | BuildOptions.CleanBuildCache
#endif
                        ,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            };

            // Switch the standalone build to the headless / server subtarget
            // so Unity strips rendering. Supported on macOS, Linux, Windows
            // 2021.3+.
#if UNITY_2021_2_OR_NEWER
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
#endif

            // Inject a scripting define so scripts can tell at compile time
            // that they are running inside a training build, in addition to
            // the runtime --env-mode=training flag that mlagents-learn sends.
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(options.targetGroup);
            string trainingDefine = "ECOCITY_TRAINING_BUILD";
            string mergedDefines = defines.Contains(trainingDefine)
                ? defines
                : string.IsNullOrEmpty(defines) ? trainingDefine : defines + ";" + trainingDefine;
            PlayerSettings.SetScriptingDefineSymbolsForGroup(options.targetGroup, mergedDefines);

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(options.targetGroup, defines);
#if UNITY_2021_2_OR_NEWER
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
#endif
            }

            if (report.summary.result == BuildResult.Succeeded)
            {
                string abs = Path.GetFullPath(outPath);
                Debug.Log($"[Eco-City] Headless build succeeded at {abs}\n" +
                          $"Train with:\n" +
                          $"  .venv/bin/mlagents-learn config/ppo/TerrainCityPlanner.yaml \\\n" +
                          $"      --run-id=eco-city-v1 --force \\\n" +
                          $"      --env=\"{abs}\" --num-envs=1 --time-scale=20 \\\n" +
                          $"      --env-args --env-mode=training");
            }
            else
            {
                Debug.LogError($"[Eco-City] Headless build failed: {report.summary.result}");
            }
        }

        private static string GetExtensionFor(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneOSX: return ".app";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.StandaloneLinux64:
                    return ".x86_64";
                default:
                    return "";
            }
        }
    }
}
#endif
