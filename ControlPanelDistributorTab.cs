using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace SubspaceTransport
{
    // Lists Subspace Restockers in the Control Panel's Distributor tab instead of the Station tab.
    //
    // The window builds its list in DetermineFilterResults, skipping station types the tab's filter doesn't
    // include. On the Distributor tab the prefix adds the local station type so restockers get past that check,
    // and a hook right after each station's type is known drops every other local station there (and drops
    // restockers from the Station tab). The restockers keep the local station entry type, so their entries and
    // inspector are the vanilla station ones.
    [HarmonyPatch(typeof(UIControlPanelWindow), "DetermineFilterResults")]
    internal static class ControlPanelDistributorTabPatch
    {
        private static bool _distributorTab;
        private static ControlPanelFilter.EEntryFilter _savedTypeFilter;

        private static void Prefix(UIControlPanelWindow __instance)
        {
            _distributorTab = false;
            ControlPanelFilter.EEntryFilter typeFilter = __instance.filter.typeFilter;
            if ((typeFilter & ControlPanelFilter.EEntryFilter.Dispenser) == 0
                || (typeFilter & ControlPanelFilter.EEntryFilter.LocalStation) != 0)
            {
                return;
            }

            _distributorTab = true;
            _savedTypeFilter = typeFilter;
            __instance.filter.typeFilter = typeFilter | ControlPanelFilter.EEntryFilter.LocalStation;
        }

        private static void Finalizer(UIControlPanelWindow __instance)
        {
            if (_distributorTab)
            {
                __instance.filter.typeFilter = _savedTypeFilter;
                _distributorTab = false;
            }
        }

        internal static bool SkipStation(StationComponent station)
        {
            return SubspaceRestockerManager.IsRestocker(station) != _distributorTab;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            object stationLocal = null;
            object skipLabel = null;
            bool afterIsCollector = false;
            bool inserted = false;

            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                if (stationLocal == null
                    && instruction.opcode == OpCodes.Ldfld
                    && instruction.operand is System.Reflection.FieldInfo field
                    && field.DeclaringType == typeof(StationComponent)
                    && field.Name == "isStellar"
                    && i > 0)
                {
                    stationLocal = code[i - 1].operand;
                }
                else if (skipLabel == null
                    && instruction.opcode == OpCodes.Bne_Un
                    && i >= 2
                    && code[i - 2].opcode == OpCodes.Ldfld
                    && (code[i - 2].operand as System.Reflection.FieldInfo)?.Name == "id"
                    && (code[i - 2].operand as System.Reflection.FieldInfo)?.DeclaringType == typeof(StationComponent))
                {
                    skipLabel = instruction.operand;
                }
                else if (instruction.opcode == OpCodes.Ldfld
                    && (instruction.operand as System.Reflection.FieldInfo)?.Name == "isCollector"
                    && (instruction.operand as System.Reflection.FieldInfo)?.DeclaringType == typeof(StationComponent))
                {
                    afterIsCollector = true;
                }
                else if (afterIsCollector && !inserted && instruction.opcode.Name.StartsWith("stloc"))
                {
                    if (stationLocal == null || skipLabel == null)
                    {
                        break;
                    }

                    CodeInstruction load = new CodeInstruction(OpCodes.Ldloc_S, stationLocal);
                    code.Insert(i + 1, load);
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ControlPanelDistributorTabPatch), nameof(SkipStation))));
                    code.Insert(i + 3, new CodeInstruction(OpCodes.Brtrue, skipLabel));
                    inserted = true;
                    break;
                }
            }

            if (!inserted)
            {
                Plugin.ModLogger.LogError("The Control Panel station list changed; restockers stay in the Station tab.");
                return code;
            }

            return code;
        }
    }
}
