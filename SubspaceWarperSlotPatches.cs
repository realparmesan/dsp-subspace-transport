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
            if (itemId != SubspaceWarperContent.ItemId || !StationSettings.IsWormholeStation(__instance))
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
            int count = StationSettings.TakeAllSubspaceWarpers(station);
            if (count > 0)
            {
                int upCount = player.TryAddItemToPackage(
                    SubspaceWarperContent.ItemId,
                    count,
                    0,
                    true,
                    station.entityId);
                UIItemup.Up(SubspaceWarperContent.ItemId, upCount);
            }
        }
    }

    [HarmonyPatch(typeof(PlanetTransport), "ThrowItems_Station")]
    internal static class SubspaceWarperThrowItemsPatch
    {
        private static void Prefix(PlanetTransport __instance, int stationId, float dropRate)
        {
            StationComponent station = SubspaceWarperSlotLifecycle.GetStation(__instance, stationId);
            int count = StationSettings.TakeAllSubspaceWarpers(station);
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

            trashSystem.AddTrashOnPlanet(
                SubspaceWarperContent.ItemId,
                (int)(count * rate),
                0,
                station.entityId,
                __instance.planet);
        }
    }

    [HarmonyPatch(typeof(PlanetTransport), "ClearItems_Station")]
    internal static class SubspaceWarperClearItemsPatch
    {
        private static void Prefix(PlanetTransport __instance, int stationId)
        {
            StationSettings.TakeAllSubspaceWarpers(SubspaceWarperSlotLifecycle.GetStation(__instance, stationId));
        }
    }

    [HarmonyPatch(typeof(PlanetTransport), "RemoveStationComponent")]
    internal static class SubspaceWarperRemoveStationPatch
    {
        private static void Prefix(PlanetTransport __instance, int id)
        {
            StationSettings.ForgetStation(SubspaceWarperSlotLifecycle.GetStation(__instance, id));
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
}
