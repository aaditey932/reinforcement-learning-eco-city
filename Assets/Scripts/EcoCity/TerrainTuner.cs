using UnityEngine;

namespace EcoCity
{
    /// <summary>
    /// Four-slider tuner that controls the terrain the Eco-City env sits on.
    /// Two sliders drive the underlying <see cref="TerrainGenerator"/> so the
    /// mesh actually has more (or fewer) mountains and valleys; the other two
    /// drive the <see cref="BiomeThresholds"/> so that what the RL env calls
    /// "water" and "mountain" lines up with what you visually see.
    ///
    /// Attach this to the same GameObject as <see cref="TerrainGenerator"/>.
    /// <see cref="EcoCityBootstrapper"/> reads the tuner on startup, applies
    /// it to the generator, and hands the resulting thresholds to the env.
    /// </summary>
    [RequireComponent(typeof(TerrainGenerator))]
    public class TerrainTuner : MonoBehaviour
    {
        [Header("Geometry")]
        [Tooltip("0 = jagged alps, 1 = pancake plains. Scales the TerrainGenerator's heightScale.")]
        [Range(0f, 1f)] public float flatness = 0.5f;

        [Tooltip("0 = one big massif, 1 = many small peaks. Maps to TerrainGenerator's noise 'scale' (inverse).")]
        [Range(0f, 1f)] public float mountainDensity = 0.5f;

        [Header("Biome classification")]
        [Tooltip("Fraction of cells that should be classified as Water. Only Green zones are allowed on Water.")]
        [Range(0f, 0.6f)] public float waterLevel = 0.15f;

        [Tooltip("Fraction of cells that should be classified as Upland+Peak. Only Industrial/Energy/Green/Road are allowed on Upland; only Green on Peak.")]
        [Range(0f, 0.6f)] public float mountainFraction = 0.20f;

        [Header("Seed")]
        [Tooltip("Perlin-noise seed. Leave at 0 to keep the generator's default.")]
        public int seed = 0;

        [Tooltip("Re-run TerrainGenerator.Initiate() whenever this component applies. Turn off if you author the mesh by hand.")]
        public bool regenerateMesh = true;

        // Snapshot of the ORIGINAL TerrainGenerator values (before this
        // component ever touched them) so re-applying the tuner at any
        // slider value starts from the same reference point instead of
        // compounding on the previously-applied values. Serialized so the
        // baseline survives domain reloads and edit-mode slider dragging.
        [HideInInspector] [SerializeField] private bool m_CapturedBaseline;
        [HideInInspector] [SerializeField] private float m_BaseHeightScale;
        [HideInInspector] [SerializeField] private float m_BaseScale;
        [HideInInspector] [SerializeField] private float m_BaseDampening;

        private TerrainGenerator m_Gen;

        private void Awake()
        {
            Apply();
        }

        public TerrainGenerator GetTerrain()
        {
            if (m_Gen == null) m_Gen = GetComponent<TerrainGenerator>();
            return m_Gen;
        }

        public BiomeThresholds ComputeThresholds()
        {
            float w = Mathf.Clamp(waterLevel, 0f, 0.6f);
            float mtn = Mathf.Clamp(mountainFraction, 0f, 0.6f);

            // Reserve the top `mtn` of the normalised elevation range for
            // Upland+Peak. Peak takes ~1/3 of that band, Upland the rest.
            float upland = Mathf.Clamp(1f - mtn, w + 0.2f, 0.98f);
            float midlandCut = Mathf.Lerp(w + 0.05f, upland - 0.05f, 0.55f);
            float lowlandCut = Mathf.Lerp(w + 0.05f, midlandCut - 0.02f, 0.4f);

            return new BiomeThresholds
            {
                water = w,
                lowland = lowlandCut,
                midland = midlandCut,
                upland = upland,
            }.Sanitized();
        }

        /// <summary>
        /// Push the current slider values into the <see cref="TerrainGenerator"/>
        /// and, if requested, rebuild the mesh. Safe to call repeatedly.
        /// </summary>
        public void Apply()
        {
            var gen = GetTerrain();
            if (gen == null) return;

            if (!m_CapturedBaseline)
            {
                m_BaseHeightScale = gen.heightScale > 0 ? gen.heightScale : 50f;
                m_BaseScale = gen.scale > 0 ? gen.scale : 34f;
                m_BaseDampening = gen.dampening > 0 ? gen.dampening : 0.21f;
                m_CapturedBaseline = true;
            }

            // flatness=0 -> 2x baseline amplitude, flatness=1 -> ~0.05x.
            float heightMul = Mathf.Lerp(2f, 0.05f, Mathf.Clamp01(flatness));
            gen.heightScale = Mathf.Clamp(m_BaseHeightScale * heightMul, 1f, 3000f);

            // Smaller TerrainGenerator.scale = higher spatial frequency = more
            // peaks per unit area. Map mountainDensity=0 -> 2x baseline scale
            // (few features) and mountainDensity=1 -> 0.4x (many features).
            float scaleMul = Mathf.Lerp(2f, 0.4f, Mathf.Clamp01(mountainDensity));
            gen.scale = Mathf.Clamp(m_BaseScale * scaleMul, 5f, 300f);

            // Dampening keeps the noise bounded. Flat terrains want a little
            // less dampening so small features still read.
            gen.dampening = Mathf.Clamp(m_BaseDampening * Mathf.Lerp(1.1f, 0.8f, flatness), 0.001f, 1f);

            if (seed != 0) gen.seed = seed;

            if (regenerateMesh)
            {
                // Seed Unity's Random so Poisson-Disc sampling produces the
                // same point set each regeneration. Without this the mesh
                // shape shifts every time a slider ticks, making it look
                // like the flatness slider isn't doing anything.
                int effectiveSeed = seed != 0 ? seed : gen.seed != 0 ? gen.seed : 12345;
                var prevState = Random.state;
                Random.InitState(effectiveSeed);
                try
                {
                    gen.Initiate();
                }
                finally
                {
                    Random.state = prevState;
                }
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Apply + Regenerate Terrain")]
        private void ApplyFromMenu()
        {
            var gen = GetTerrain();
            if (gen == null) { Debug.LogError("[TerrainTuner] No TerrainGenerator sibling."); return; }
            Apply();
            Debug.Log($"[TerrainTuner] Regenerated. heightScale={gen.heightScale:F1}, scale={gen.scale:F1}, dampening={gen.dampening:F3}");
        }

        [ContextMenu("Recapture Baseline From Current TerrainGenerator")]
        private void RecaptureBaseline()
        {
            m_CapturedBaseline = false;
            var gen = GetTerrain();
            if (gen == null) return;
            // Force a capture right now so subsequent Apply calls are
            // relative to whatever's currently in the TerrainGenerator.
            m_BaseHeightScale = gen.heightScale > 0 ? gen.heightScale : 50f;
            m_BaseScale = gen.scale > 0 ? gen.scale : 34f;
            m_BaseDampening = gen.dampening > 0 ? gen.dampening : 0.21f;
            m_CapturedBaseline = true;
            Apply();
        }

        private void OnValidate()
        {
            // NOTE: we deliberately do NOT reset m_CapturedBaseline here,
            // otherwise each slider tweak would treat the previously-applied
            // values as the new baseline and compound. The baseline is
            // captured once on the first Apply() and stays sticky via
            // [SerializeField]. Use the "Recapture Baseline" context menu
            // item if you manually change the TerrainGenerator values.

            if (!isActiveAndEnabled) return;

            // OnValidate fires during serialization; calling Initiate()
            // synchronously can trigger "SendMessage cannot be called during
            // Awake/OnValidate" errors. Defer one editor tick.
            UnityEditor.EditorApplication.delayCall += DeferredApply;
        }

        private void DeferredApply()
        {
            UnityEditor.EditorApplication.delayCall -= DeferredApply;
            if (this == null) return;
            Apply();
        }
#endif
    }
}
