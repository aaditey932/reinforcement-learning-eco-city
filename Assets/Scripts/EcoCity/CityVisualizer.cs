using UnityEngine;

namespace EcoCity
{
    /// <summary>
    /// Paints a colored semi-transparent tile on the terrain for every grid
    /// cell that has a non-empty zone assigned. Each tile is a primitive
    /// quad aligned flat to the world, positioned at the cell's XZ center
    /// and vertically snapped to the terrain surface via a downward raycast
    /// onto the <see cref="TerrainGenerator"/>'s <see cref="MeshCollider"/>.
    ///
    /// Colors come from <see cref="CityMetricsUtility.ZoneColor"/>; transparency
    /// is applied via a per-tile <see cref="MaterialPropertyBlock"/> so all
    /// 144 tiles share a single material and keep draw call count low.
    /// </summary>
    public class CityVisualizer : MonoBehaviour
    {
        [Header("References")]
        public TerrainCityEnvironment environment;
        public TerrainGenerator terrain;

        [Header("Tile appearance")]
        [Tooltip("Base alpha for placed zone tiles (0 = invisible, 1 = opaque).")]
        [Range(0f, 1f)] public float tileAlpha = 0.55f;

        [Tooltip("Scale of the quad relative to one grid cell.")]
        [Range(0.5f, 1.0f)] public float tileSizeFraction = 0.92f;

        [Tooltip("Vertical offset above the sampled terrain height.")]
        public float tileHeightOffset = 0.35f;

        [Tooltip("Optional override material. If null, a runtime transparent unlit material is used.")]
        public Material overrideMaterial;

        private GameObject[,] m_Tiles;
        private MaterialPropertyBlock m_Block;
        private Material m_Material;
        private int m_GridSize;
        private bool m_Subscribed;

        private void Awake()
        {
            m_Block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            SubscribeAndBuild();
        }

        private void OnDisable()
        {
            if (environment != null && m_Subscribed)
            {
                environment.OnGridChanged -= OnGridChanged;
                environment.OnTerrainRegenerated -= OnTerrainRegenerated;
                m_Subscribed = false;
            }
        }

        private void Start()
        {
            // The environment might have been assigned after Awake (e.g. by a
            // bootstrapper). Re-attempt wiring here.
            SubscribeAndBuild();
        }

        public void SubscribeAndBuild()
        {
            if (environment == null) environment = FindObjectOfType<TerrainCityEnvironment>();
            if (environment == null) return;
            if (terrain == null) terrain = environment.terrain;

            environment.EnsureInitialized();

            if (!m_Subscribed)
            {
                environment.OnGridChanged += OnGridChanged;
                environment.OnTerrainRegenerated += OnTerrainRegenerated;
                m_Subscribed = true;
            }

            if (m_Tiles == null || m_GridSize != environment.gridSize)
                BuildTiles();

            OnGridChanged(environment.Grid);
        }

        /// <summary>
        /// Called when the environment rebuilds its terrain mid-run (per-
        /// episode reroll). Tile XY positions stay the same but their Y
        /// snaps to the new terrain heights, and we start hidden until the
        /// policy starts placing zones on the fresh grid.
        /// </summary>
        private void OnTerrainRegenerated()
        {
            BuildTiles();
        }

        private Material GetMaterial()
        {
            if (overrideMaterial != null) return overrideMaterial;
            if (m_Material != null) return m_Material;

            // Unity's built-in Sprites/Default shader is present in every
            // project and supports vertex color + transparency. Using it
            // avoids shipping a custom shader asset.
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            m_Material = new Material(shader) { name = "CityTile (runtime)" };
            return m_Material;
        }

        private void BuildTiles()
        {
            ClearTiles();

            m_GridSize = environment.gridSize;
            m_Tiles = new GameObject[m_GridSize, m_GridSize];

            var sampler = environment.Sampler;
            float cellSize = sampler.cellWorldSize;
            float quadSize = cellSize * tileSizeFraction;

            var material = GetMaterial();

            for (int r = 0; r < m_GridSize; r++)
            for (int c = 0; c < m_GridSize; c++)
            {
                var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tile.name = $"Tile_{r}_{c}";
                tile.transform.SetParent(transform, worldPositionStays: false);

                // Flatten the quad face-up.
                tile.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                tile.transform.localScale = new Vector3(quadSize, quadSize, 1f);

                Vector3 center = sampler.CellCenter(r, c);
                center.y = sampler.heights[r, c] + tileHeightOffset;
                tile.transform.position = center;

                var col = tile.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var mr = tile.GetComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.enabled = false; // hidden until a zone is placed on this cell

                m_Tiles[r, c] = tile;
            }
        }

        private void OnGridChanged(ZoneType[,] grid)
        {
            if (m_Tiles == null) return;
            for (int r = 0; r < m_GridSize; r++)
            for (int c = 0; c < m_GridSize; c++)
            {
                var zone = grid[r, c];
                var tile = m_Tiles[r, c];
                if (tile == null) continue;
                var mr = tile.GetComponent<MeshRenderer>();
                if (zone == ZoneType.Empty)
                {
                    mr.enabled = false;
                    continue;
                }
                mr.enabled = true;
                Color color = CityMetricsUtility.ZoneColor(zone);
                color.a = tileAlpha;
                mr.GetPropertyBlock(m_Block);
                m_Block.SetColor("_Color", color);
                m_Block.SetColor("_BaseColor", color);    // URP name, harmless if unused
                m_Block.SetColor("_TintColor", color);    // legacy
                mr.SetPropertyBlock(m_Block);
            }
        }

        private void ClearTiles()
        {
            if (m_Tiles == null) return;
            for (int r = 0; r < m_Tiles.GetLength(0); r++)
            for (int c = 0; c < m_Tiles.GetLength(1); c++)
            {
                if (m_Tiles[r, c] != null)
                {
                    if (Application.isPlaying) Destroy(m_Tiles[r, c]);
                    else DestroyImmediate(m_Tiles[r, c]);
                }
            }
            m_Tiles = null;
        }
    }
}
