using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SubspaceTransport
{
    // Mirrors vanilla's Space Warper slot: move one Subspace Warper per tick from a storage row into the slot.
    [HarmonyPatch(typeof(StationComponent), "InternalTickRemote")]
    internal static class SubspaceWarperStorageRefillPatch
    {
        private static void Postfix(StationComponent __instance)
        {
            StationStore[] storage = __instance.storage;
            if (storage == null || !StationSettings.HasSubspaceWarperSpace(__instance))
            {
                return;
            }

            lock (storage)
            {
                for (int i = 0; i < storage.Length; i++)
                {
                    if (storage[i].itemId != SubspaceWarperContent.ItemId || storage[i].count <= 0)
                    {
                        continue;
                    }

                    int inc = storage[i].inc / storage[i].count;
                    storage[i].count--;
                    storage[i].inc -= inc;
                    StationSettings.AddSubspaceWarpers(__instance, 1);
                    return;
                }
            }
        }
    }

    // needs[5] is the slot belts and sorters fill outside the storage rows (vanilla uses it for Space Warpers).
    // When the Space Warper slot also wants items, alternate ticks so both slots fill.
    [HarmonyPatch(typeof(StationComponent), "UpdateNeeds")]
    internal static class SubspaceWarperNeedsPatch
    {
        private static void Postfix(StationComponent __instance)
        {
            int[] needs = __instance.needs;
            if (needs == null || needs.Length < 6 || !SubspaceLogisticsTechnology.IsUnlocked)
            {
                return;
            }

            if (needs[5] != 0 && (GameMain.gameTick & 1L) == 0L)
            {
                return;
            }

            if (StationSettings.HasSubspaceWarperSpace(__instance))
            {
                needs[5] = SubspaceWarperContent.ItemId;
            }
        }
    }

    [HarmonyPatch(typeof(StationComponent), "InputItem")]
    internal static class SubspaceWarperInputItemPatch
    {
        private static bool Prefix(StationComponent __instance, int itemId, int needIdx, int stack)
        {
            if (itemId != SubspaceWarperContent.ItemId || !StationSettings.HasSubspaceWarperSlot(__instance))
            {
                return true;
            }

            StationStore[] storage = __instance.storage;
            if (storage != null && needIdx >= 0 && needIdx < storage.Length && storage[needIdx].itemId == itemId)
            {
                return true;
            }

            StationSettings.AddSubspaceWarpers(__instance, stack);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(PlanetFactory),
        "InsertInto",
        new[] { typeof(int), typeof(int), typeof(int), typeof(byte), typeof(byte), typeof(byte) },
        new[]
        {
            ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal,
            ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out
        })]
    internal static class SubspaceWarperSorterInsertPatch
    {
        private static bool Prefix(
            PlanetFactory __instance,
            int entityId,
            int itemId,
            byte itemCount,
            ref byte remainInc,
            ref int __result)
        {
            if (itemId != SubspaceWarperContent.ItemId
                || __instance.entityPool == null
                || entityId <= 0
                || entityId >= __instance.entityPool.Length)
            {
                return true;
            }

            int stationId = __instance.entityPool[entityId].stationId;
            return !SubspaceWarperInsert.TryInsert(__instance, stationId, itemCount, ref remainInc, ref __result);
        }
    }

    [HarmonyPatch(
        typeof(PlanetFactory),
        "InsertInto",
        new[] { typeof(uint), typeof(int), typeof(int), typeof(byte), typeof(byte), typeof(byte) },
        new[]
        {
            ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal,
            ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out
        })]
    internal static class SubspaceWarperTypedInsertPatch
    {
        private static bool Prefix(
            PlanetFactory __instance,
            uint ioTargetTypedId,
            int itemId,
            byte itemCount,
            ref byte remainInc,
            ref int __result)
        {
            if (itemId != SubspaceWarperContent.ItemId
                || (EFactoryIOTargetType)(ioTargetTypedId & 0xFF000000u) != EFactoryIOTargetType.Station)
            {
                return true;
            }

            int stationId = (int)(ioTargetTypedId & 0xFFFFFF);
            return !SubspaceWarperInsert.TryInsert(__instance, stationId, itemCount, ref remainInc, ref __result);
        }
    }

    internal static class SubspaceWarperInsert
    {
        internal static bool TryInsert(
            PlanetFactory factory,
            int stationId,
            byte itemCount,
            ref byte remainInc,
            ref int result)
        {
            StationComponent[] stationPool = factory.transport?.stationPool;
            if (stationId <= 0 || stationPool == null || stationId >= stationPool.Length)
            {
                return false;
            }

            StationComponent station = stationPool[stationId];
            if (station == null || station.id != stationId || !StationSettings.HasSubspaceWarperSpace(station))
            {
                return false;
            }

            StationSettings.AddSubspaceWarpers(station, itemCount);
            remainInc = 0;
            result = itemCount;
            return true;
        }
    }

    [HarmonyPatch(typeof(PlanetTransport), "TakeBackItems_Station")]
    internal static class SubspaceWarperTakeBackPatch
    {
        private static void Prefix(PlanetTransport __instance, Player player, int stationId)
        {
            if (player == null)
            {
                return;
            }

            StationComponent station = SubspaceWarperSlotLifecycle.GetStation(__instance, stationId);
            GiveBack(player, station, SubspaceWarperContent.ItemId, StationSettings.TakeAllSubspaceWarpers(station));
            GiveBack(
                player,
                station,
                SubspaceRestockerContent.DroneItemId,
                StationSettings.TakeSubspaceDrones(station, int.MaxValue));
        }

        private static void GiveBack(Player player, StationComponent station, int itemId, int count)
        {
            if (count > 0)
            {
                int upCount = player.TryAddItemToPackage(itemId, count, 0, true, station.entityId);
                UIItemup.Up(itemId, upCount);
            }
        }
    }

    [HarmonyPatch(typeof(PlanetTransport), "ThrowItems_Station")]
    internal static class SubspaceWarperThrowItemsPatch
    {
        private static void Prefix(PlanetTransport __instance, int stationId, float dropRate)
        {
            StationComponent station = SubspaceWarperSlotLifecycle.GetStation(__instance, stationId);
            Drop(
                __instance,
                station,
                SubspaceWarperContent.ItemId,
                StationSettings.TakeAllSubspaceWarpers(station),
                dropRate);
            Drop(
                __instance,
                station,
                SubspaceRestockerContent.DroneItemId,
                StationSettings.TakeSubspaceDrones(station, int.MaxValue),
                dropRate);
        }

        private static void Drop(PlanetTransport transport, StationComponent station, int itemId, int count, float dropRate)
        {
            TrashSystem trashSystem = GameMain.data != null ? GameMain.data.trashSystem : null;
            if (count <= 0 || trashSystem == null)
            {
                return;
            }

            float rate = dropRate * UnityEngine.Random.Range(0.8f, 1.2f);
            if (rate > 1f)
            {
                rate = 1f;
            }

            trashSystem.AddTrashOnPlanet(itemId, (int)(count * rate), 0, station.entityId, transport.planet);
        }
    }

    [HarmonyPatch(typeof(PlanetTransport), "ClearItems_Station")]
    internal static class SubspaceWarperClearItemsPatch
    {
        private static void Prefix(PlanetTransport __instance, int stationId)
        {
            StationComponent station = SubspaceWarperSlotLifecycle.GetStation(__instance, stationId);
            StationSettings.TakeAllSubspaceWarpers(station);
            StationSettings.TakeSubspaceDrones(station, int.MaxValue);
        }
    }

    [HarmonyPatch(typeof(PlanetTransport), "RemoveStationComponent")]
    internal static class SubspaceWarperRemoveStationPatch
    {
        private static void Prefix(PlanetTransport __instance, int id)
        {
            StationComponent station = SubspaceWarperSlotLifecycle.GetStation(__instance, id);
            StationSettings.ForgetStation(station);
            SubspaceRestockerManager.OnStationRemoved(station);
        }
    }

    internal static class SubspaceWarperSlotLifecycle
    {
        internal static StationComponent GetStation(PlanetTransport transport, int stationId)
        {
            StationComponent[] stationPool = transport?.stationPool;
            if (stationId <= 0 || stationPool == null || stationId >= stationPool.Length)
            {
                return null;
            }

            StationComponent station = stationPool[stationId];
            return station != null && station.id == stationId ? station : null;
        }
    }

    // A belt slot's storageIdx picks what the slot outputs: 0 is nothing, 1..N a storage row, N + 1 the Space
    // Warper. Subspace Warpers use -2, which vanilla treats as "nothing", so it skips such slots and the patches
    // below do the output.
    internal static class SubspaceWarperBeltOutput
    {
        internal const int StorageIdx = -2;

        internal static bool IsOffered(StationComponent station)
        {
            return SubspaceLogisticsTechnology.IsUnlocked && StationSettings.HasSubspaceWarperSlot(station);
        }
    }

    [HarmonyPatch(typeof(StationComponent), "UpdateOutputSlots")]
    internal static class SubspaceWarperBeltOutputPatch
    {
        // Runs before vanilla so the slot's cooldown behaves like vanilla's: an insert sets it to 1, and
        // vanilla's decrement in the same call takes this 2 down to 1.
        private static void Prefix(StationComponent __instance, CargoTraffic traffic)
        {
            if (__instance.isVeinCollector || __instance.slots == null || !SubspaceWarperBeltOutput.IsOffered(__instance))
            {
                return;
            }

            lock (__instance.storage)
            {
                for (int i = 0; i < __instance.slots.Length; i++)
                {
                    ref SlotData slot = ref __instance.slots[i];
                    if (slot.dir != IODir.Output
                        || slot.storageIdx != SubspaceWarperBeltOutput.StorageIdx
                        || slot.counter > 0
                        || slot.beltId == 0)
                    {
                        continue;
                    }

                    CargoPath path = traffic.GetCargoPath(traffic.beltPool[slot.beltId].segPathId);
                    if (path == null
                        || path.buffer[9] != 0
                        || StationSettings.GetSubspaceWarperCount(__instance) <= 0
                        || !path.TryInsertItemAtHeadAndFillBlank(SubspaceWarperContent.ItemId, 1, 0))
                    {
                        continue;
                    }

                    StationSettings.TryConsumeSubspaceWarper(__instance);
                    slot.counter = 2;
                }
            }
        }

        // Vanilla blanks the belt's sign for a slot it treats as "nothing"; show the Subspace Warper instead.
        private static void Postfix(StationComponent __instance, CargoTraffic traffic, SignData[] signPool, bool active)
        {
            if (!active
                || __instance.isVeinCollector
                || __instance.slots == null
                || !SubspaceWarperBeltOutput.IsOffered(__instance))
            {
                return;
            }

            for (int i = 0; i < __instance.slots.Length; i++)
            {
                SlotData slot = __instance.slots[i];
                if (slot.dir == IODir.Output
                    && slot.storageIdx == SubspaceWarperBeltOutput.StorageIdx
                    && slot.beltId != 0)
                {
                    int entityId = traffic.beltPool[slot.beltId].entityId;
                    signPool[entityId].iconType = 1u;
                    signPool[entityId].iconId0 = (uint)SubspaceWarperContent.ItemId;
                }
            }
        }
    }

    // Adds the Subspace Warper after the storage rows and the Space Warper in both belt filter menus: the slot
    // picker opened by clicking a station's belt port, and the tip shown while dragging a belt out of a station.
    // Both share the vanilla fields filterItems, selectedIndex, outputEntityId and outputSlotId.
    [HarmonyPatch]
    internal static class SubspaceWarperSlotPickerPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UISlotPicker), "SetOutputEntity");
            yield return AccessTools.Method(typeof(UIBeltBuildTip), "SetOutputEntity");
        }

        // Inserted just before vanilla resets selectedIndex, once the Space Warper entry has been (or not been) added.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            string name = original.DeclaringType.Name;
            FieldInfo isStellar = AccessTools.Field(typeof(StationComponent), "isStellar");
            FieldInfo filterItems = AccessTools.Field(original.DeclaringType, "filterItems");
            FieldInfo selectedIndex = AccessTools.Field(original.DeclaringType, "selectedIndex");

            int stellarIndex = code.FindIndex(instruction => instruction.LoadsField(isStellar));
            if (stellarIndex < 1)
            {
                Plugin.ModLogger?.LogError(name + " filter patch: could not find the Space Warper check.");
                return code;
            }

            CodeInstruction stationLoad = code[stellarIndex - 1];
            int storeIndex = code.FindIndex(stellarIndex, instruction => instruction.StoresField(selectedIndex));
            if (storeIndex < 2)
            {
                Plugin.ModLogger?.LogError(name + " filter patch: could not find the selection reset.");
                return code;
            }

            // code[storeIndex - 2] is the ldarg.0 starting "selectedIndex = 0"; branch targets land on it.
            CodeInstruction anchor = code[storeIndex - 2];
            CodeInstruction first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(anchor.labels);
            anchor.labels.Clear();
            code.InsertRange(storeIndex - 2, new[]
            {
                first,
                new CodeInstruction(OpCodes.Ldfld, filterItems),
                new CodeInstruction(stationLoad.opcode, stationLoad.operand),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(SubspaceWarperSlotPickerPatch), nameof(AddSubspaceWarper)))
            });
            return code;
        }

        internal static void AddSubspaceWarper(List<int> filterItems, StationComponent station)
        {
            if (SubspaceWarperBeltOutput.IsOffered(station))
            {
                filterItems.Add(SubspaceWarperContent.ItemId);
            }
        }

        // Vanilla returns early when the menu already shows this slot; the belt tool calls it every frame.
        private static void Prefix(object __instance, int entityId, int slot, out bool __state)
        {
            Traverse menu = Traverse.Create(__instance);
            __state = menu.Field("outputEntityId").GetValue<int>() != entityId
                || menu.Field("outputSlotId").GetValue<int>() != slot;
        }

        // The vanilla index clamp turns the negative storageIdx into "nothing"; select the last entry instead.
        // Only when the menu was rebuilt, so the player can still move the selection away from it.
        private static void Postfix(object __instance, int entityId, int slot, bool __state)
        {
            PlanetFactory factory = GameMain.mainPlayer?.factory;
            if (!__state || factory == null || entityId <= 0 || slot < 0)
            {
                return;
            }

            int stationId = factory.entityPool[entityId].stationId;
            StationComponent station = stationId > 0 ? factory.transport.stationPool[stationId] : null;
            if (station == null || slot >= station.slots.Length
                || station.slots[slot].storageIdx != SubspaceWarperBeltOutput.StorageIdx)
            {
                return;
            }

            Traverse menu = Traverse.Create(__instance);
            List<int> filterItems = menu.Field("filterItems").GetValue<List<int>>();
            if (filterItems != null && filterItems.Count > 0
                && filterItems[filterItems.Count - 1] == SubspaceWarperContent.ItemId)
            {
                menu.Field("selectedIndex").SetValue(filterItems.Count - 1);
            }
        }
    }

    // Vanilla stores the selected index as the slot's storageIdx; store -2 when the Subspace Warper is selected.
    [HarmonyPatch]
    internal static class SubspaceWarperSlotPickerSelectPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UISlotPicker), "SetFilterToEntity");
            yield return AccessTools.Method(typeof(UIBeltBuildTip), "SetFilterToEntity");
        }

        private static void Postfix(object __instance)
        {
            Traverse menu = Traverse.Create(__instance);
            List<int> filterItems = menu.Field("filterItems").GetValue<List<int>>();
            int selectedIndex = menu.Field("selectedIndex").GetValue<int>();
            int entityId = menu.Field("outputEntityId").GetValue<int>();
            int slot = menu.Field("outputSlotId").GetValue<int>();
            PlanetFactory factory = GameMain.mainPlayer?.factory;
            if (filterItems == null || factory == null || entityId <= 0 || slot < 0
                || selectedIndex < 0 || selectedIndex >= filterItems.Count
                || filterItems[selectedIndex] != SubspaceWarperContent.ItemId)
            {
                return;
            }

            int stationId = factory.entityPool[entityId].stationId;
            StationComponent station = stationId > 0 ? factory.transport.stationPool[stationId] : null;
            if (station != null && slot < station.slots.Length)
            {
                station.slots[slot].storageIdx = SubspaceWarperBeltOutput.StorageIdx;
            }
        }
    }
}
