using System;
using HarmonyLib;

namespace SubspaceTransport
{
    [HarmonyPatch(typeof(StationComponent), "DetermineDispatch")]
    internal static class DetermineDispatchPatch
    {
        [ThreadStatic]
        private static StationComponent _virtualDispatchStation;

        private struct DispatchState
        {
            internal int OriginalIdleShipCount;
            internal bool AddedVirtualShip;
            internal int OriginalWarperCount;
            internal bool AddedVirtualWarper;
        }

        private static void Prefix(StationComponent __instance, out DispatchState __state)
        {
            __state = new DispatchState
            {
                OriginalIdleShipCount = __instance.idleShipCount,
                AddedVirtualShip = false,
                OriginalWarperCount = __instance.warperCount,
                AddedVirtualWarper = false
            };

            if (SubspaceLogisticsTechnology.CurrentLevel > 0
                && __instance.isStellar
                && StationSettings.IsEnabled(__instance)
                && InstantTransport.HasSubspaceWarper(__instance))
            {
                if (__state.OriginalIdleShipCount == 0)
                {
                    __state.AddedVirtualShip = true;
                    __instance.idleShipCount = 1;
                }

                if (__state.OriginalWarperCount == 0)
                {
                    __state.AddedVirtualWarper = true;
                    __instance.warperCount = 1;
                }

                if (__state.AddedVirtualShip || __state.AddedVirtualWarper)
                {
                    _virtualDispatchStation = __instance;
                }
            }
        }

        private static Exception Finalizer(
            StationComponent __instance,
            DispatchState __state,
            Exception __exception)
        {
            if (__state.AddedVirtualShip)
            {
                __instance.idleShipCount = __state.OriginalIdleShipCount;
            }

            if ((__state.AddedVirtualShip || __state.AddedVirtualWarper)
                && _virtualDispatchStation == __instance)
            {
                _virtualDispatchStation = null;
            }

            if (__state.AddedVirtualWarper)
            {
                __instance.warperCount = __state.OriginalWarperCount;
            }

            return __exception;
        }

        internal static bool IsUsingVirtualDispatch(StationComponent station)
        {
            return ReferenceEquals(_virtualDispatchStation, station);
        }
    }

    [HarmonyPatch(typeof(StationComponent), "DispatchSupplyShip")]
    internal static class DispatchSupplyShipPatch
    {
        private static bool Prefix(
            StationComponent __instance,
            int carryCnt,
            int storagePairSupplyItemId,
            bool takeWarper,
            StationComponent other,
            int[] consumeRegister,
            ref SupplyDemandPair pair,
            ref bool __result)
        {
            if (!CanUseInstantTransport(__instance, other, storagePairSupplyItemId))
            {
                if (DetermineDispatchPatch.IsUsingVirtualDispatch(__instance))
                {
                    __result = false;
                    return false;
                }

                return true;
            }

            __result = InstantTransport.TryTransfer(
                __instance,
                pair.supplyIndex,
                other,
                pair.demandIndex,
                storagePairSupplyItemId,
                carryCnt,
                __instance,
                consumeRegister);

            return false;
        }

        private static bool CanUseInstantTransport(
            StationComponent source,
            StationComponent destination,
            int itemId)
        {
            return SubspaceLogisticsTechnology.CurrentLevel > 0
                && source != null
                && destination != null
                && InstantTransport.HasSubspaceWarper(source)
                && source.isStellar
                && destination.isStellar
                && StationSettings.IsEnabled(source)
                && StationSettings.IsEnabled(destination)
                && source.gid != destination.gid
                && itemId != SubspaceWarperContent.ItemId;
        }
    }

    [HarmonyPatch(typeof(StationComponent), "DispatchDemandShip")]
    internal static class DispatchDemandShipPatch
    {
        private static bool Prefix(
            StationComponent __instance,
            int shipCarries,
            bool takeWarper,
            StationComponent other,
            int[] consumeRegister,
            ref SupplyDemandPair pair,
            ref bool __result)
        {
            int itemId = __instance.storage[pair.demandIndex].itemId;
            if (!CanUseInstantTransport(__instance, other, itemId))
            {
                if (DetermineDispatchPatch.IsUsingVirtualDispatch(__instance))
                {
                    __result = false;
                    return false;
                }

                return true;
            }

            __result = InstantTransport.TryTransfer(
                other,
                pair.supplyIndex,
                __instance,
                pair.demandIndex,
                itemId,
                shipCarries,
                __instance,
                consumeRegister);

            return false;
        }

        private static bool CanUseInstantTransport(
            StationComponent requester,
            StationComponent supplier,
            int itemId)
        {
            return SubspaceLogisticsTechnology.CurrentLevel > 0
                && requester != null
                && supplier != null
                && InstantTransport.HasSubspaceWarper(requester)
                && requester.isStellar
                && supplier.isStellar
                && StationSettings.IsEnabled(requester)
                && StationSettings.IsEnabled(supplier)
                && requester.gid != supplier.gid
                && itemId != SubspaceWarperContent.ItemId;
        }
    }

    [HarmonyPatch(typeof(StationComponent), "CalcTripEnergyCost")]
    internal static class CalcTripEnergyCostPatch
    {
        private static void Postfix(StationComponent __instance, ref long __result)
        {
            if (!__instance.isStellar)
            {
                return;
            }

            int level = SubspaceLogisticsTechnology.CurrentLevel;
            if (level <= 0 || !StationSettings.IsEnabled(__instance))
            {
                return;
            }

            int multiplier = SubspaceLogisticsTechnology.GetEnergyMultiplier(level);
            if (multiplier > 1)
            {
                __result = __result > long.MaxValue / multiplier
                    ? long.MaxValue
                    : __result * multiplier;
            }
        }
    }

    internal static class InstantTransport
    {
        internal static bool TryTransfer(
            StationComponent source,
            int sourceIndex,
            StationComponent destination,
            int destinationIndex,
            int itemId,
            int maximumCount,
            StationComponent transportStation,
            int[] consumeRegister)
        {
            if (source.storage == null
                || destination.storage == null
                || sourceIndex < 0
                || sourceIndex >= source.storage.Length
                || destinationIndex < 0
                || destinationIndex >= destination.storage.Length
                || itemId <= 0
                || maximumCount <= 0)
            {
                return false;
            }

            StationStore[] firstStorage = source.gid < destination.gid
                ? source.storage
                : destination.storage;
            StationStore[] secondStorage = source.gid < destination.gid
                ? destination.storage
                : source.storage;

            int transferredCount;
            lock (firstStorage)
            {
                lock (secondStorage)
                {
                    StationStore sourceStore = source.storage[sourceIndex];
                    StationStore destinationStore = destination.storage[destinationIndex];

                    if (sourceStore.itemId != itemId || destinationStore.itemId != itemId)
                    {
                        return false;
                    }

                    int sourceAvailable = Math.Min(sourceStore.count, sourceStore.remoteSupplyCount);
                    int destinationDemand = Math.Min(
                        destinationStore.remoteDemandCount,
                        destinationStore.max - destinationStore.count);
                    transferredCount = Math.Min(
                        maximumCount,
                        Math.Min(sourceAvailable, destinationDemand));

                    if (transferredCount <= 0)
                    {
                        return false;
                    }

                    if (!StationSettings.TryConsumeSubspaceWarper(transportStation))
                    {
                        return false;
                    }

                    int transferredInc = SplitInc(
                        sourceStore.count,
                        sourceStore.inc,
                        transferredCount);

                    sourceStore.count -= transferredCount;
                    sourceStore.inc -= transferredInc;
                    destinationStore.count += transferredCount;
                    destinationStore.inc += transferredInc;

                    source.storage[sourceIndex] = sourceStore;
                    destination.storage[destinationIndex] = destinationStore;
                    if (consumeRegister != null && consumeRegister.Length > SubspaceWarperContent.ItemId)
                    {
                        lock (consumeRegister)
                        {
                            consumeRegister[SubspaceWarperContent.ItemId]++;
                        }
                    }
                }
            }

            RecordTransfer(source, destination, itemId, transferredCount);
            return true;
        }

        internal static bool HasSubspaceWarper(StationComponent station)
        {
            return StationSettings.GetSubspaceWarperCount(station) > 0;
        }

        private static int SplitInc(int count, int inc, int transferCount)
        {
            if (count <= 0)
            {
                return 0;
            }

            int perItemInc = inc / count;
            int remainder = inc - perItemInc * count;
            int remainingCount = count - transferCount;
            int transferRemainder = remainder - remainingCount;
            return transferRemainder > 0
                ? perItemInc * transferCount + transferRemainder
                : perItemInc * transferCount;
        }

        private static void RecordTransfer(
            StationComponent source,
            StationComponent destination,
            int itemId,
            int count)
        {
            GameData data = GameMain.data;
            if (data == null || data.statistics == null || data.statistics.traffic == null)
            {
                return;
            }

            TrafficStatistics traffic = data.statistics.traffic;
            traffic.RegisterPlanetOutputStat(source.planetId, itemId, count);
            traffic.RegisterPlanetInputStat(destination.planetId, itemId, count);

            int sourceStarId = source.planetId / 100;
            int destinationStarId = destination.planetId / 100;
            if (sourceStarId == destinationStarId)
            {
                traffic.RegisterStarInternalStat(sourceStarId, itemId, count);
                traffic.RegisterStarInternalStat(destinationStarId, itemId, count);
            }
            else
            {
                traffic.RegisterStarOutputStat(sourceStarId, itemId, count);
                traffic.RegisterStarInputStat(destinationStarId, itemId, count);
            }
        }
    }
}
