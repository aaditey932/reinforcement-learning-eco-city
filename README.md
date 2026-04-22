# Eco-City RL

**Eco-City RL** learns urban zoning policies on a simulated city grid with **reinforcement learning**, on **procedurally generated low-poly terrain** in **Unity**. The policy must balance **livability**, **pollution**, **traffic**, and **energy** over many steps. A useful lens on the project is **reward alignment**: pathologies like do-nothing or degenerate equilibria can appear even in a toy planner.

## Stack & workflow

| Piece | What we use |
| --- | --- |
| **Slides** | [Google Slides deck](https://docs.google.com/presentation/d/1IwpBpNnXW4747Yx1HLHcz8JSwdsLau5sChvJ3IdvafU/edit?slide=id.p11#slide=id.p11) (opens slide 11). |
| **Video** | [Google Drive recording](https://drive.google.com/file/d/1vkpW3IULQ_QqL-IRJByOXp5jArCXBq8h/view?usp=share_link). |
| **Engine** | Unity (environment + visuals). |
| **RL** | **Unity ML-Agents** — **PPO** trained with `mlagents-learn` and [`config/ppo/TerrainCityPlanner.yaml`](config/ppo/TerrainCityPlanner.yaml). |
| **Training** | Python side launches the trainer; Unity (editor or **headless build**) runs the agent and collects rollouts. |
| **Inference** | **Unity** loads the exported **ONNX** policy; zone choices are shown as coloured tiles on the terrain (`CityVisualizer`). |
| **Terrain** | `TerrainGenerator` + optional `TerrainTuner`; **each episode can use a newly randomised terrain** (see below). |

Step-by-step commands: [`training/README.md`](training/README.md).

**What’s in Git:** `Assets/`, `Packages/`, `ProjectSettings/` (version + build scenes), [`config/`](config/ppo/TerrainCityPlanner.yaml), Python [`requirements.txt`](requirements.txt), [`training/README.md`](training/README.md), and sample [`Exports/`](Exports/). **`Library/` is not tracked** (Unity regenerates it). **`UserSettings/`** and **ML-Agents `Timers/` JSON** are ignored as machine-local. On first open, Unity may add or rewrite additional files under `ProjectSettings/`; the editor version is pinned in `ProjectSettings/ProjectVersion.txt` (**Unity 2022.3 LTS** family — align with [`Packages/manifest.json`](Packages/manifest.json)).

---

## Why urban planning & RL?

- Cities account for a large share of **global emissions** and most people live in them.
- **Land-use** (residential, commercial, industrial, green, roads, energy) **locks in** traffic, pollution, and lifestyle for a long time.
- Simulators can predict outcomes **given** a plan; **RL** targets a **policy** that chooses actions over time when the problem is **sequential**, **path-dependent**, and **only partly comparable** to a static “optimal layout” dataset.

---

## Literature grounding

This prototype sits at the intersection of **urban systems science**, **microscopic simulation**, and **deep RL tooling**. The bullets below are not an exhaustive survey; they anchor the README’s claims in widely cited sources.

| Theme | Why it matters here | Representative sources |
| --- | --- | --- |
| **Cities and mitigation** | Motivation: urban form and infrastructure strongly influence emissions, mobility, and resource use over long horizons. | IPCC **AR6 WG III** on mitigation in urban and settlement systems ([IPCC AR6 WGIII](https://www.ipcc.ch/report/ar6/wg3/)); UN **World Cities Report** / **SDG 11** framing for urbanization pressure ([UN-Habitat](https://unhabitat.org/)). |
| **Land use & integrated models** | “Given a layout, what happens?” is the classic **LUTI** / operational land-use modelling question; tools forecast travel, activity, and environmental outcomes under scenarios. | Waddell et al. on **UrbanSim** as an integrated land use–transport–environment microsimulation ([UrbanSim](https://www.urbansim.com/)); broader **LUTI** surveys (e.g. Wegener, *Handbook of Regional Science*, land-use–transport interaction models). |
| **Sequential decision-making** | Zoning unfolds over time; policies are **Markov** or **POMDP**-style decision processes with delayed consequences—standard RL formalism. | Sutton & Barto, *Reinforcement Learning: An Introduction* (MDPs, policy improvement); Puterman, *Markov Decision Processes* (formal planning under uncertainty). |
| **On-policy deep RL for control** | **PPO** is a stable default for discrete actions with function approximation; Unity’s trainer implements this stack. | Schulman et al., “Proximal Policy Optimization Algorithms” ([arXiv:1707.06347](https://arxiv.org/abs/1707.06347)). |
| **Embodied / engine-based training** | Training in Unity matches the **train where you simulate** pattern used in games and robotics curricula. | Juliani et al., “Unity: A General Platform for Intelligent Agents” ([arXiv:1809.02627](https://arxiv.org/abs/1809.02627)); Unity **ML-Agents** [documentation](https://github.com/Unity-Technologies/ml-agents). |
| **Scalar rewards & safety framing** | Multi-term city scores are a **proxy objective**; degenerate policies under imperfect rewards mirror the “misspecified objective / negative side effect” discussion in AI safety. | Amodei et al., “Concrete Problems in AI Safety” ([arXiv:1606.06565](https://arxiv.org/abs/1606.06565)); Krakovna on [specification gaming](https://deepmind.google/discover/blog/specification-gaming-the-flip-side-of-ai-ingenuity/); multi-objective RL surveys (e.g. Roijers et al., *JAIR* 2013, multi-objective sequential decision-making) for **Pareto** extensions beyond a single weighted sum. |

**Caveat:** Real municipalities combine politics, law, and stakeholder conflict resolution—this repo is a **toy grid** for methods exploration, not a substitute for participatory planning or calibrated regional models.

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
- **Alignment-style issues** (sparse placement, reward gaming, weight sensitivity) are still worth analysing on top of this env; treat any informal scalar results as **illustrative** unless tied to a logged `run-id`.

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
