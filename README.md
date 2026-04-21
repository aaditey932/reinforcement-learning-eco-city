# Eco-City RL

**Eco-City RL** learns urban zoning policies on a simulated city grid with **reinforcement learning**. The objective is to balance **livability**, **pollution**, **traffic**, and **energy** while placing zones over many steps. A central theme of the work is **reward alignment**: classic failure modes (do-nothing equilibria, “minimalist” policies that game sparse bonuses) show up even in a small grid world.

**Stack (presentation v3):** [PPO](https://stable-baselines3.readthedocs.io/) from **Stable-Baselines3**, a custom **Gymnasium** environment, and a **Three.js** viewer for rollout visualization.

This repo may still include **Unity / ML-Agents** bits; [`training/README.md`](training/README.md) documents that workflow if present. The **MDP, reward story, results, and alignment** sections below match the **v3 talking script** (~11 slides / ~7 min).

---

## Why urban planning & RL?

- Cities drive a large share of **global CO₂** and host most of the world’s population.
- **Land-use choices** (residential vs. industry vs. roads vs. green vs. energy) **lock in** emissions, traffic, and equity outcomes for a long time.
- Tools like **UrbanSim** can simulate outcomes **given** a layout, but they do not by themselves produce an **optimized decision policy**. **RL** fills that gap when you want a **sequential** plan: the problem is **path-dependent**, reward is **delayed**, there is no simple dataset of “optimal cities per state,” and the deliverable is a **decision rule**, not only a forward model.

---

## MDP overview

### State (804-d float32 → MLP policy)

| Component | Description |
| --- | --- |
| **Zone grid** | **10×10** cells; each cell is a **7-D one-hot** for zone type → **700** floats. |
| **Buildable mask** | Binary flag per cell from **terrain height** (no steep or water tiles) → **100** floats. |
| **Global scalars** | **Population**, **pollution**, **traffic congestion**, **energy balance** — normalized (e.g. **VecNormalize**). → **4** floats. |

**Total: 804** dimensions, fed to an **MLP** policy.

### Action (35 discrete)

Structured as **cell slot × zone type**:

- **index = cell_slot × 7 + zone_type**
- **5** candidate **buildable** cells per step × **7** zone types: **empty**, residential, commercial, industrial, green, road, energy → **35** actions.
- **Masking:** only **buildable** candidates are used each step. Without this, the agent wastes moves on invalid terrain and learning **stalls**.

### Transition (rule-based, deterministic)

- **Industry** raises **pollution** each step; dense industry spikes penalties.
- **Green** applies **mitigation** near industry — the livability vs. cleanup trade-off.
- **Roads** interact with **traffic** from nearby residential/commercial density.
- **Energy** zones add **supply**; residential and commercial add **demand** — co-plan power or the **energy mismatch** penalty compounds.
- **Episode length:** **200** steps; **terrain** is **regenerated on reset**.

---

## Reward (five terms — each has a “failure story”)

Weighted sum; typical roles:

| Term | Idea |
| --- | --- |
| **Livability** | **α × (population / 100)** — must not be capped in a way that makes “build nothing” optimal (see alignment below). |
| **Pollution** | **−β × total emissions** — if **β** is too low vs. livability, “factory cities” win. |
| **Traffic** | **−γ × congestion** — without it, residential packs without roads. |
| **Energy** | **−δ × \|supply − demand\|** — forces co-planning of power with demand. |
| **Build bonus** | **+0.01** per placed cell — breaks the **empty-grid / zero-reward** equilibrium when other terms incentivize inaction. |

Designing these weights (e.g. **Experiment 2:** sustainability vs. growth) is where **growth vs. sustainability** trade-offs are explored.

---

## PPO training setup

- **Algorithm:** **PPO** with **MLP actor–critic** (e.g. two hidden layers of **64** units, **ReLU**), **categorical** head over **35** actions.
- **VecNormalize** on observations/rewards (with a sensible **clip**, e.g. **10**) — important for **stability**; **explained variance** moving up (e.g. toward ~**0.7**) is a health signal that the critic is useful.
- Example hyperparameters used in reporting: **lr** 3e-4, **n_steps** 2048, **batch_size** 64, **γ** 0.99, **GAE λ** 0.95, **clip** 0.2, **entropy** 0.01, **10** epochs per update; on the order of **500k** timesteps (e.g. ~**20 minutes** on a Colab **A100**).
- **Training curve:** e.g. episode reward mean improving from very negative toward less negative (e.g. **−6k → −1.6k**) depending on reward normalization.

---

## Results (indicative numbers from evaluation)

On a **200-step** deterministic eval, order-of-magnitude story:

| Agent | Typical cumulative reward |
| --- | ---: |
| **Random** | ~**−4,750** |
| **Greedy** (argmax livability each step) | ~**−6,324** — worse than random; ignores pollution/traffic long term |
| **Best hand-crafted heuristic** | ~**−1,911** |
| **PPO** | **+21.08** — only method **above zero** in this run; large margin over baselines |

**Hyperparameter sensitivity:** e.g. **lr** 1e-4 can give **+21.28** while default **3e-4** may collapse (e.g. **−265**) if updates are too aggressive; **high entropy** can crater performance (e.g. **−2,900**). Diagnostics like **high clip fraction** and **approximate KL** align with “use a smaller learning rate.”

**Seeds:** e.g. across **20** seeds, top rollouts staying **positive** (**+43**, **+20**, **+7**) supports checking robustness (with caveats below).

---

## Alignment & failure modes (the “real story”)

1. **Do-nothing Nash equilibrium** — Early runs converged to an **all-empty** grid because livability was **capped** while penalties were **unbounded**, so **zero** beat any “real” city. **Fix:** **uncap** livability appropriately and add the **build bonus** → positive reward and actual construction.

2. **Minimalist policy** — After fixes, the agent may place only ~**11** cells (e.g. mostly **roads + green**), **no** residential/industry — **zero** population ⇒ **zero** pollution/traffic/energy stress; reward dominated by **build bonus** (~**+22**). **Correct under the reward**, but shows **incomplete** reward design.

3. **Diagnostics before sweeps** — High **clip fraction** / **KL** foreshadowed that **lower lr** would help; sweeps matched that intuition.

### Sim-to-real & gaps (honest scope)

| Topic | Prototype | Real-world extension |
| --- | --- | --- |
| Reward | Weight sweeps (e.g. Exp. 2) | Constrained RL, reward models from stakeholders |
| Distribution shift | e.g. **1.5×** population/emissions tests (Exp. 3) | Need stronger metrics; sparse policies may look “robust” by not reacting |
| Equity | Global metrics only | **Pareto** / multi-objective policies per neighbourhood |
| Observation | Full state | **Partial observability** → recurrent policies; sensor/census lag |
| Scale | **10×10** | **Hierarchical RL**, **GNN** policies, larger grids |

Politics, budgets, and legal constraints are not captured; **single-agent PPO** here is a **decision-support** core, not a full urban governance model.

---

## Conclusion & next steps

- **RL** fits **sequential multi-objective planning**; **PPO** can beat strong baselines by a large margin when rewards are workable.
- The **most informative** artefacts are often **what the reward did** (empty → minimalist road/green) — **alignment in miniature**.
- **Next steps:** **per-resident livability** to break minimalist optima, **hierarchical PPO** for scale, **warm-start** from heuristics, **offline RL** from real land-use data. **Reward design** is where alignment work actually lives.

---

## Q&A (short)

- **Why not DQN?** **35** discrete actions with a **high-dimensional continuous** observation — **PPO + VecNormalize** is typically more stable than tabular-style Q on this setup.
- **Does robustness under shift mean generalization?** **Ambiguous** — could be true robustness or a **sparse** policy that barely interacts with the regime; motivates richer metrics (e.g. per-resident livability).
- **Why didn’t tuning change much?** Multiple settings can hit the **same minimalist ceiling**; next gains are likely **reward re-specification**, not another grid search on PPO alone.

---

## Credits & terrain visual (optional asset)

![low-poly terrain preview](https://raw.githubusercontent.com/KristinLague/KristinLague.github.io/main/Images/lowPolyTerrainGIF.gif)

- Low-poly terrain inspiration: Kristin Lague’s [Low-Poly-Terrain-Generator](https://github.com/KristinLague/Low-Poly-Terrain-Generator) (MIT), with Triangle.NET triangulation in some Unity-based workflows.
