using UnityEngine;

namespace EcoCity
{
    /// <summary>
    /// Zone types the agent can place on a cell. Empty is the reset state and
    /// is not directly selectable as an action target (the action space only
    /// covers the placeable types).
    /// </summary>
    public enum ZoneType
    {
        Empty = 0,
        Residential = 1,
        Commercial = 2,
        Industrial = 3,
        Green = 4,
        Road = 5,
        Energy = 6,
    }

    /// <summary>
    /// Terrain-derived biome band for a cell, classified by sampled elevation
    /// at the cell's world-space center. Determines which zones are allowed.
    /// </summary>
    public enum BiomeBand
    {
        Water = 0,
        Lowland = 1,
        Midland = 2,
        Upland = 3,
        Peak = 4,
    }

    /// <summary>
    /// Returned from <see cref="TerrainCityEnvironment.Step"/>.
    /// </summary>
    public struct CityStepResult
    {
        public float reward;
        public bool terminated;
    }

    /// <summary>
    /// Static helpers for zone-space arithmetic. The action space covers the
    /// six placeable zones (Residential..Energy); <see cref="ZoneType.Empty"/>
    /// is reserved as the reset value.
    /// </summary>
    public static class CityMetricsUtility
    {
        /// <summary>Number of placeable zones (used to size the action space).</summary>
        public const int NumPlaceableZones = 6;

        /// <summary>Zone one-hot length in the observation (includes Empty).</summary>
        public const int NumZoneTypesWithEmpty = 7;

        /// <summary>Alias kept for compatibility with the original stub.</summary>
        public const int NumZoneTypes = NumPlaceableZones;

        public const int NumBiomeBands = 5;

        public static ZoneType PlaceableZoneFromIndex(int index)
        {
            // 0..5 -> Residential..Energy
            return (ZoneType)(index + 1);
        }

        public static int PlaceableZoneToIndex(ZoneType zone)
        {
            return ((int)zone) - 1;
        }

        /// <summary>
        /// Biome-band allow-list per zone. Gentle lowlands/midlands support
        /// almost everything; water and peaks are nature-only.
        /// </summary>
        public static bool IsZoneAllowedInBiome(ZoneType zone, BiomeBand band)
        {
            switch (band)
            {
                case BiomeBand.Water:
                    return zone == ZoneType.Green;
                case BiomeBand.Peak:
                    return zone == ZoneType.Green;
                case BiomeBand.Lowland:
                    return zone == ZoneType.Residential
                        || zone == ZoneType.Commercial
                        || zone == ZoneType.Green
                        || zone == ZoneType.Road
                        || zone == ZoneType.Energy;
                case BiomeBand.Midland:
                    return zone == ZoneType.Residential
                        || zone == ZoneType.Commercial
                        || zone == ZoneType.Industrial
                        || zone == ZoneType.Green
                        || zone == ZoneType.Road
                        || zone == ZoneType.Energy;
                case BiomeBand.Upland:
                    return zone == ZoneType.Industrial
                        || zone == ZoneType.Green
                        || zone == ZoneType.Road
                        || zone == ZoneType.Energy;
            }
            return false;
        }

        /// <summary>Fixed visualization palette for transparent tiles.</summary>
        public static Color ZoneColor(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Residential: return new Color(0.310f, 0.639f, 1.000f); // #4FA3FF
                case ZoneType.Commercial:  return new Color(1.000f, 0.722f, 0.310f); // #FFB84F
                case ZoneType.Industrial:  return new Color(0.847f, 0.357f, 0.357f); // #D85B5B
                case ZoneType.Green:       return new Color(0.373f, 0.784f, 0.475f); // #5FC879
                case ZoneType.Road:        return new Color(0.604f, 0.627f, 0.651f); // #9AA0A6
                case ZoneType.Energy:      return new Color(0.961f, 0.851f, 0.286f); // #F5D949
            }
            return new Color(1f, 1f, 1f, 0f);
        }
    }
}
