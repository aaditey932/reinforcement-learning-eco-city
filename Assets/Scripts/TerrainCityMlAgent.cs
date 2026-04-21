using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;
using EcoCity;

/// <summary>
/// ML-Agents bridge for the Eco-City environment. Mirrors the structure of
/// the ML-Agents Crawler example: Agent owns a reference to a sibling
/// environment component, requests decisions every step, writes an action
/// mask, and forwards the reward + termination flag back to the Academy.
///
/// Observation:  <see cref="TerrainCityEnvironment.GetObservation"/>.
/// Action:       single discrete branch of size <see cref="TerrainCityEnvironment.ActionSpaceSize"/>
///               (= gridSize * gridSize * 6). Invalid combinations are masked.
/// Reward:       per-step delta of the weighted multi-objective score.
/// </summary>
[RequireComponent(typeof(TerrainCityEnvironment))]
public class TerrainCityMlAgent : Agent
{
    [Header("References")]
    public TerrainCityEnvironment environment;

    [Header("Exploration")]
    [Tooltip("Probability of taking a random valid action instead of the policy's action. Useful when recording demos; leave at 0 for training/inference.")]
    [Range(0f, 1f)] public float explorationEpsilon = 0f;

    public override void Initialize()
    {
        if (environment == null)
            environment = GetComponent<TerrainCityEnvironment>();
        environment.EnsureInitialized();
        MaxStep = environment.maxSteps;
    }

    public override void OnEpisodeBegin()
    {
        if (environment == null) environment = GetComponent<TerrainCityEnvironment>();
        environment.ResetEnvironment();
        MaxStep = environment.maxSteps;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        float[] obs = environment.GetObservation();
        for (int i = 0; i < obs.Length; i++)
            sensor.AddObservation(obs[i]);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        int n = environment.ActionSpaceSize;
        bool anyValid = false;
        for (int a = 0; a < n; a++)
        {
            bool valid = environment.IsActionValid(a);
            if (valid) anyValid = true;
            if (!valid) actionMask.SetActionEnabled(0, a, false);
        }
        // Never leave every action masked — ML-Agents asserts that at least
        // one action per branch is enabled.
        if (!anyValid)
        {
            actionMask.SetActionEnabled(0, 0, true);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];

        if (explorationEpsilon > 0f && Random.value < explorationEpsilon)
            action = SampleRandomValidAction(environment);

        var result = environment.Step(action);
        AddReward(result.reward);

        if (result.terminated)
            EndEpisode();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        actionsOut.DiscreteActions.Array[0] = SampleRandomValidAction(environment);
    }

    private static int SampleRandomValidAction(TerrainCityEnvironment env)
    {
        int n = env.ActionSpaceSize;
        // Rejection sample first — cheap on average since most of the space
        // is valid.
        for (int i = 0; i < 256; i++)
        {
            int a = Random.Range(0, n);
            if (env.IsActionValid(a)) return a;
        }
        // Fall back to linear scan so we always return a legal action.
        for (int a = 0; a < n; a++)
            if (env.IsActionValid(a)) return a;
        return 0;
    }

#if UNITY_EDITOR
    [ContextMenu("Auto-Configure BehaviorParameters")]
    private void AutoConfigureBehaviorParameters()
    {
        if (environment == null) environment = GetComponent<TerrainCityEnvironment>();
        environment.EnsureInitialized();

        var bp = GetComponent<BehaviorParameters>();
        if (bp == null)
        {
            Debug.LogError("[TerrainCityMlAgent] No BehaviorParameters component on this GameObject.");
            return;
        }

        bp.BehaviorName = "TerrainCityPlanner";
        bp.BrainParameters.VectorObservationSize = environment.ObservationSize;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(environment.ActionSpaceSize);

        UnityEditor.EditorUtility.SetDirty(bp);
        Debug.Log($"[TerrainCityMlAgent] BehaviorParameters configured: " +
                  $"VectorObs={environment.ObservationSize}, DiscreteActions=[{environment.ActionSpaceSize}]");
    }
#endif
}
