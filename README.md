# Eco-City RL

**Eco-City RL** learns urban zoning policies on a simulated city grid with **reinforcement learning**, on **procedurally generated low-poly terrain** in **Unity**. The policy must balance **livability**, **pollution**, **traffic**, and **energy** over many steps. A useful lens on the project is **reward alignment**: pathologies like do-nothing or degenerate equilibria can appear even in a toy planner.

## Stack & workflow

| Piece | What we use |
| --- | --- |
| **Engine** | Unity (environment + visuals). |
| **RL** | **Unity ML-Agents** — **PPO** trained with `mlagents-learn` and [`config/ppo/TerrainCityPlanner.yaml`](config/ppo/TerrainCityPlanner.yaml). |
| **Training** | Python side launches the trainer; Unity (editor or **headless build**) runs the agent and collects rollouts. |
| **Inference** | **Unity** loads the exported **ONNX** policy; zone choices are shown as coloured tiles on the terrain (`CityVisualizer`). |
| **Terrain** | `TerrainGenerator` + optional `TerrainTuner`; **each episode can use a newly randomised terrain** (see below). |

Step-by-step commands: [`training/README.md`](training/README.md).

**What’s in Git:** `Assets/`, `Packages/`, `ProjectSettings/` (version + build scenes), [`config/`](config/ppo/TerrainCityPlanner.yaml), Python [`requirements.txt`](requirements.txt), [`training/README.md`](training/README.md), sample [`Exports/`](Exports/), and slide assets. **`Library/` is not tracked** (Unity regenerates it). **`UserSettings/`** and **ML-Agents `Timers/` JSON** are ignored as machine-local. On first open, Unity may add or rewrite additional files under `ProjectSettings/`; the editor version is pinned in `ProjectSettings/ProjectVersion.txt` (**Unity 2022.3 LTS** family — align with [`Packages/manifest.json`](Packages/manifest.json)).

---

## Why urban planning & RL?

- Cities account for a large share of **global emissions** and most people live in them.
- **Land-use** (residential, commercial, industrial, green, roads, energy) **locks in** traffic, pollution, and lifestyle for a long time.
- Simulators can predict outcomes **given** a plan; **RL** targets a **policy** that chooses actions over time when the problem is **sequential**, **path-dependent**, and **only partly comparable** to a static “optimal layout” dataset.

---

## MDP (Unity implementation)

Defaults come from [`TerrainCityEnvironment`](Assets/Scripts/EcoCity/TerrainCityEnvironment.cs) (`gridSize`, `maxSteps`, etc.).

### Observation

Flattened **ML-Agents** vector per decision:

| Block | Role |
| --- | --- |
| **Zone one-hot** | For each cell: **7** floats (empty + six placeable zone types) → `gridSize² × 7`. |
| **Global metrics** | Livability, pollution, traffic, energy mismatch — **4** floats. |
| **Zone share** | Fraction of cells per **placeable** zone type — **6** floats. |
| **Biome encoding** | Per-cell biome band as a **normalised scalar** — `gridSize²` floats. |

For the default **`gridSize = 12`**, that is **12×12×7 + 4 + 6 + 144 = 1162** floats. (If you change `gridSize`, `ObservationSize` updates accordingly.)

### Action

Single **discrete** branch of size **`gridSize × gridSize × 6`** (default **12×12×6 = 864**). Mapping:

- `action = cellIndex * 6 + placeableZoneIndex`
- `cellIndex = row * gridSize + col`
- **Action masking** hides biome-disallowed placements and **no-op re-placements** (putting the same zone on a cell that already has it).

### Transition & terrain randomisation

- Dynamics are **rule-based** given the current terrain: `CityMetrics` scores the grid; zones interact with pollution, traffic, and energy (see codebase for full rules).
- **Episode length:** `maxSteps` (default **400**).
- **Random terrain each episode:** `TerrainCityEnvironment.randomTerrainEachEpisode` (default **on**) re-seeds the terrain (via `TerrainTuner` when attached), **rebuilds the mesh**, and re-samples **biome bands** so the agent does not memorise a single height field. Optional `initialTerrainSeed` keeps the **first** episode reproducible.

---

## Reward

Per-step reward is the **change** in a **weighted score** between successive metrics snapshots (**delta shaping**), so learning is not dominated by episode length when the grid is sparse:

```text
weighted = alpha * livability - beta * pollution - gamma * traffic - delta * energyMismatch
R_t = weighted(t) - weighted(t-1)
```

Default weights on `TerrainCityEnvironment` / `RewardWeights`: **α=1.0, β=1.2, γ=0.7, δ=0.8** (tunable for experiments). Experiment 3 exposes **demand** and **pollution-penalty** multipliers for stress tests.

---

## PPO (ML-Agents)

Training hyperparameters live in **`config/ppo/TerrainCityPlanner.yaml`** (e.g. PPO with **two** layers of **256** hidden units, `normalize: true`, `time_horizon` tied to the behaviour, `max_steps` budget, etc.). Behaviour name **`TerrainCityPlanner`** must match **Behavior Parameters** in Unity.

---

## Baselines & experiments

- **Baselines** (random / greedy / heuristic): [`BaselinePolicies.cs`](Assets/Scripts/EcoCity/BaselinePolicies.cs), editor tooling under [`Assets/Scripts/EcoCity/Editor/`](Assets/Scripts/EcoCity/Editor/).
- **Alignment-style issues** (sparse placement, reward gaming, weight sensitivity) are still worth analysing on top of this env; treat any slide-specific scalar results as **illustrative** unless tied to a logged `run-id`.

---

## Inference in Unity

After training, import **`TerrainCityPlanner.onnx`** from the ML-Agents results folder, assign it to the bootstrapper / behavior, set **Inference Only**, and press Play — tiles show **Residential**, **Commercial**, **Industrial**, **Green**, **Road**, **Energy** on the current terrain.

| Zone | Colour |
| --- | --- |
| Residential | `#4FA3FF` |
| Commercial | `#FFB84F` |
| Industrial | `#D85B5B` |
| Green | `#5FC879` |
| Road | `#9AA0A6` |
| Energy | `#F5D949` |

---

## Q&A pointers

- **Why PPO here?** Large discrete action space + long vector observations; ML-Agents PPO with masking fits the setup well.
- **Does random terrain each episode mean generalisation?** It **reduces overfitting to one mesh** but does not replace real-world validation — policies can still exploit quirks of the synthesised generator.

---

## Credits

![low-poly terrain preview](https://raw.githubusercontent.com/KristinLague/KristinLague.github.io/main/Images/lowPolyTerrainGIF.gif)

- Terrain generation: Kristin Lague’s [`Low-Poly-Terrain-Generator`](https://github.com/KristinLague/Low-Poly-Terrain-Generator) (MIT), with **Triangle.NET** for triangulation in this project.
- **RL:** Unity **ML-Agents** (see `requirements.txt` / package manifest for versions).
