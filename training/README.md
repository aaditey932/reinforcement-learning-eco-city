# Training the Eco-City PPO agent

This project reuses the ML-Agents Crawler training workflow: you start
`mlagents-learn` with a PPO config on the Python side, then press Play in
the Unity editor (or launch a built player) so the Unity `Agent` connects
back over gRPC.

## 1. One-time setup

```bash
# from the repo root
python3.9 -m venv .venv
.venv/bin/pip install -r requirements.txt
```

Open the project in **Unity 2022.2.0b8** (or any Unity 2022.3 LTS). The
package manifest already declares `com.unity.ml-agents@2.3.0-exp.3` which
matches `mlagents==0.30.0` (ML-Agents Release 21). Unity will resolve and
import it on the first open; accept any API upgrade prompts.

Open `Assets/Scenes/SampleScene.unity`, select `Tools -> Eco-City ->
Spawn Eco-City In Scene`. This creates an `EcoCity` GameObject with
`EcoCityBootstrapper`, which in turn spawns the environment, agent, and
visualizer under it at Play time.

## 2. Launch training

You have two paths. Use **A (editor)** for debugging / first runs; use
**B (headless build)** for long training sessions — it's roughly 5-10x
faster because nothing is rendered.

### A. In-editor training (quick iteration)

```bash
.venv/bin/mlagents-learn config/ppo/TerrainCityPlanner.yaml \
    --run-id=eco-city-v1 --force --time-scale=20
```

Then press Play in the Unity editor. The `mlagents-learn` process
connects to the Unity `Agent` and begins collecting rollouts. Before
pressing Play you can also tick **Fast Training** on
`EcoCityBootstrapper` — this disables the visualizer, drops quality to
level 0, and makes the editor run unthrottled.

### B. Headless standalone build (fast training)

1. Open the scene that already has an `EcoCityBootstrapper` in it.
2. Run `Tools -> Eco-City -> Build Headless Training Player`.

   This builds a server-subtarget standalone of the current scene into
   `Builds/EcoCityTraining.{app,exe,x86_64}`. The build is compiled
   with the scripting define `ECOCITY_TRAINING_BUILD`, so on launch the
   bootstrapper automatically:
   * turns on fast-training mode,
   * disables the `CityVisualizer`,
   * drops to quality level 0,
   * runs uncapped (no vsync).

3. Train against the built player:

   ```bash
   .venv/bin/mlagents-learn config/ppo/TerrainCityPlanner.yaml \
       --run-id=eco-city-v1 --force \
       --env="Builds/EcoCityTraining" \
       --num-envs=1 --time-scale=20 \
       --env-args --env-mode=training
   ```

   `--num-envs=N` runs N copies of the built player in parallel. On a
   laptop 1-2 is a good starting point; on a workstation you can push
   to 4-8 if each env is small (this project's 12x12 grid fits easily).
   `--time-scale` is forwarded to Unity via ML-Agents' engine
   configuration channel and overrides the bootstrapper default.
   `--env-args --env-mode=training` forwards the `--env-mode=training`
   flag into each player process (the build honours this even without
   the scripting define as a belt-and-braces fallback).

Stop with `Ctrl+C` once you're happy with the reward curve, or wait for
`max_steps` (2,000,000 by default).

Training artifacts land under `results/eco-city-v1/`. The exported policy
will be `results/eco-city-v1/TerrainCityPlanner.onnx`.

## 3. Run inference (with coloured transparent tiles)

1. In Unity, drag `results/eco-city-v1/TerrainCityPlanner.onnx` into
   `Assets/` so Unity imports it as an `NNModel`.
2. Select the `EcoCity` GameObject. On `EcoCityBootstrapper` set:
   - `Onnx Model` = the imported asset
   - `Behavior Type` = `InferenceOnly`
3. Press Play. The `CityVisualizer` child paints a coloured semi-
   transparent quad on each cell as the policy places zones:

   | Zone         | Colour                     |
   |--------------|----------------------------|
   | Residential  | soft blue   `#4FA3FF`      |
   | Commercial   | amber       `#FFB84F`      |
   | Industrial   | red         `#D85B5B`      |
   | Green        | green       `#5FC879`      |
   | Road         | gray        `#9AA0A6`      |
   | Energy       | yellow      `#F5D949`      |

## 4. Run experiments

- **Experiment 1 (baselines vs PPO)**: `Tools -> Eco-City -> Run Baseline
  Benchmark` runs Random, GreedyPopulation and BalancedHeuristic policies
  for 50 episodes each and writes `Exports/baselines.csv`. Running the
  trained ONNX model over the same number of episodes (via `Tools ->
  Eco-City -> Benchmark Trained Model`) appends a `PPO` row.
- **Experiment 2 (reward sensitivity)**: `Tools -> Eco-City -> Sweep
  Reward Weights` varies `alpha` and `beta` in a small grid and exports
  `Exports/reward_sweep.csv` plus the final zoning grid as JSON per run.
- **Experiment 3 (generalization)**: adjust `demandMultiplier` and
  `pollutionPenaltyMultiplier` on `TerrainCityEnvironment` and re-run the
  trained model benchmark.
- **Experiment 4 (policy behavior)**: per-step zoning snapshots are
  captured in each benchmark run under `Exports/snapshots/`.

## Troubleshooting

- *"Couldn't connect to Unity environment"* (editor path A): make sure you
  pressed Play in Unity **after** `mlagents-learn` printed "Listening on
  port 5004".
- *Headless build fails with "scripting backend not installed"* (path B):
  install the Mac/Linux/Windows Dedicated Server build-support module for
  your Unity version via the Hub (Unity -> Add Modules -> e.g. "Mac Build
  Support (IL2CPP) — Dedicated Server").
- *Action-mask assertion* on the first step: open the `EcoCity` GameObject,
  verify the terrain is assigned on `TerrainCityEnvironment`. A missing
  terrain falls back to all-Midland biome bands where every zone is legal,
  so the mask will always have at least one valid action.
- *Training is still slow in the editor*: tick **Fast Training** on the
  `EcoCityBootstrapper` component and relaunch `mlagents-learn` with
  `--time-scale=20`. For best results use the headless build path.
