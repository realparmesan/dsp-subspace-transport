using System;
using System.Reflection;
using HarmonyLib;

namespace SubspaceTransport
{
    // Optional interop with More Megastructures. A star can hold either a Battery Sphere or one of its
    // megastructures, never both; each mod's selector refuses to switch a star the other has claimed.
    internal static class MoreMegaStructureCompat
    {
        internal const string PluginGuid = "Gnimaerd.DSP.plugin.MoreMegaStructure";

        private const int DysonSphereType = 0;

        private static FieldInfo _starTypesField;
        private static FieldInfo _currentStarField;

        internal static bool IsPresent
        {
            get { return _starTypesField != null; }
        }

        internal static void Initialize(Harmony harmony)
        {
            Type plugin = AccessTools.TypeByName("MoreMegaStructure.MoreMegaStructure");
            if (plugin == null)
            {
                return;
            }

            FieldInfo starTypes = AccessTools.Field(plugin, "StarMegaStructureType");
            FieldInfo currentStar = AccessTools.Field(plugin, "curStar");
            MethodInfo setMegaStructure = AccessTools.Method(plugin, "SetMegaStructure", new[] { typeof(int) });
            if (starTypes == null || starTypes.FieldType != typeof(int[])
                || currentStar == null || currentStar.FieldType != typeof(StarData)
                || setMegaStructure == null)
            {
                Plugin.ModLogger.LogWarning(
                    "More Megastructures is installed but its API has changed; Battery Sphere interop is disabled.");
                return;
            }

            _starTypesField = starTypes;
            _currentStarField = currentStar;
            harmony.Patch(
                setMegaStructure,
                new HarmonyMethod(typeof(MoreMegaStructureCompat), nameof(SetMegaStructurePrefix)));
            Plugin.ModLogger.LogInfo("More Megastructures detected; Battery Sphere interop enabled.");
        }

        // True when More Megastructures has planned something other than a Dyson Sphere at the star.
        internal static bool HasMegastructure(int starIndex)
        {
            if (!IsPresent)
            {
                return false;
            }

            int[] starTypes = _starTypesField.GetValue(null) as int[];
            return starTypes != null
                && starIndex >= 0
                && starIndex < starTypes.Length
                && starTypes[starIndex] != DysonSphereType;
        }

        private static bool SetMegaStructurePrefix(int type)
        {
            StarData star = _currentStarField.GetValue(null) as StarData;
            if (star == null || type == DysonSphereType || !BatterySphereManager.IsPlanned(star.index))
            {
                return true;
            }

            UIRealtimeTip.Popup("This star has a Battery Sphere planned. Switch it to a Dyson Sphere first.");
            return false;
        }
    }
}
