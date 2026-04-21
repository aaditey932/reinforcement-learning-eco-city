#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EcoCity.EditorTools
{
    /// <summary>
    /// Editor convenience: spawn the full Eco-City stack into the active scene
    /// with a single click. Mirrors what you'd do by manually adding
    /// <see cref="EcoCityBootstrapper"/> to an empty GameObject but is faster
    /// during demos.
    /// </summary>
    public static class EcoCityMenu
    {
        [MenuItem("Tools/Eco-City/Spawn Eco-City In Scene")]
        public static void SpawnEcoCity()
        {
            var terrain = Object.FindObjectOfType<TerrainGenerator>();
            if (terrain == null)
            {
                if (!EditorUtility.DisplayDialog(
                    "No TerrainGenerator",
                    "No TerrainGenerator was found in the current scene. The Eco-City environment will run with a flat fallback biome. Continue anyway?",
                    "Continue", "Cancel"))
                {
                    return;
                }
            }

            var existing = Object.FindObjectOfType<EcoCityBootstrapper>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Eco-City] Scene already has an EcoCityBootstrapper; selected it.");
                return;
            }

            var go = new GameObject("EcoCity");
            var boot = go.AddComponent<EcoCityBootstrapper>();
            boot.terrain = terrain;

            if (terrain != null && terrain.GetComponent<TerrainTuner>() == null)
            {
                Undo.AddComponent<TerrainTuner>(terrain.gameObject);
                Debug.Log("[Eco-City] Added TerrainTuner to " + terrain.name +
                          ". Adjust flatness / mountainDensity / waterLevel / mountainFraction on it to reshape the world.");
            }

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Spawn Eco-City");
            Debug.Log("[Eco-City] Bootstrapper added to scene. Press Play to run.");
        }

        [MenuItem("Tools/Eco-City/Add Terrain Tuner")]
        public static void AddTerrainTuner()
        {
            var terrain = Object.FindObjectOfType<TerrainGenerator>();
            if (terrain == null)
            {
                EditorUtility.DisplayDialog(
                    "No TerrainGenerator",
                    "Couldn't find a TerrainGenerator in the current scene. The tuner only makes sense attached to one.",
                    "OK");
                return;
            }

            var existing = terrain.GetComponent<TerrainTuner>();
            if (existing != null)
            {
                Selection.activeGameObject = terrain.gameObject;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[Eco-City] TerrainTuner already attached to {terrain.name}; selected it.");
                return;
            }

            Undo.AddComponent<TerrainTuner>(terrain.gameObject);
            Selection.activeGameObject = terrain.gameObject;
            Debug.Log($"[Eco-City] Added TerrainTuner to {terrain.name}. Adjust the sliders in the Inspector.");
        }

        [MenuItem("Tools/Eco-City/Select Terrain Tuner")]
        public static void SelectTerrainTuner()
        {
            var tuner = Object.FindObjectOfType<TerrainTuner>();
            if (tuner == null)
            {
                AddTerrainTuner();
                return;
            }
            Selection.activeGameObject = tuner.gameObject;
            EditorGUIUtility.PingObject(tuner);
        }
    }
}
#endif
