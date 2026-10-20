using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SubspaceTransport
{
    // Vanilla only dispatches when the station has an idle vessel and, for trips past the warp distance with
    // "Warpers Required" on, two Space Warpers and warp drive research. Wormhole transfers need neither, so for
    // enabled stations those conditions are lifted for the duration of DetermineDispatch. The dispatch patches
    // below then refuse any vessel dispatch that vanilla would not have allowed.
    [HarmonyPatch(typeof(StationComponent), "DetermineDispatch")]
    internal static class DetermineDispatchPatch
    {
        [ThreadStatic]
        private static StationComponent _virtualDispatchStation;

        [ThreadStatic]
        private static bool _addedVirtualShip;

        [ThreadStatic]
        private static bool _clearedWarperNecessary;

        [ThreadStatic]
        private static bool _canWarp;

        private struct DispatchState
        {
            internal int OriginalIdleShipCount;
            internal bool AddedVirtualShip;
            internal bool ClearedWarperNecessary;
        }

        private static void Prefix(
            StationComponent __instance,
            float shipSailSpeed,
            float shipWarpSpeed,
            out DispatchState __state)
        {
            __state = new DispatchState
            {
                OriginalIdleShipCount = __instance.idleShipCount,
                AddedVirtualShip = false,
                ClearedWarperNecessary = false
            };

            if (!SubspaceLogisticsTechnology.IsUnlocked || !StationSettings.IsEnabled(__instance))
            {
                return;
            }

            if (__instance.idleShipCount == 0)
            {
                __state.AddedVirtualShip = true;
                __instance.idleShipCount = 1;
            }

            if (__instance.warperNecessary)
            {
                __state.ClearedWarperNecessary = true;
                __instance.warperNecessary = false;
            }

            if (__state.AddedVirtualShip || __state.ClearedWarperNecessary)
            {
                _virtualDispatchStation = __instance;
                _addedVirtualShip = __state.AddedVirtualShip;
                _clearedWarperNecessary = __state.ClearedWarperNecessary;
                _canWarp = shipWarpSpeed > shipSailSpeed + 1f;
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

            if (__state.ClearedWarperNecessary)
            {
                __instance.warperNecessary = true;
            }

            if (ReferenceEquals(_virtualDispatchStation, __instance))
            {
                _virtualDispatchStation = null;
            }

            return __exception;
        }

        // True when a vessel dispatch got past DetermineDispatch only because of the lifted conditions.
        internal static bool IsVesselDispatchBlocked(StationComponent station, bool takeWarper)
        {
            if (!ReferenceEquals(_virtualDispatchStation, station))
            {
                return false;
            }

            return _addedVirtualShip
                || (_clearedWarperNecessary && takeWarper && (station.warperCount < 2 || !_canWarp));
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
            if (!InstantTransport.CanTransfer(__instance, other, storagePairSupplyItemId))
            {
                if (DetermineDispatchPatch.IsVesselDispatchBlocked(__instance, takeWarper))
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
                consumeRegister);

            return false;
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

            // The requesting station dispatches here, but the cargo (and the Subspace Warper) leaves from other.
            if (!InstantTransport.CanTransfer(other, __instance, itemId))
            {
                if (DetermineDispatchPatch.IsVesselDispatchBlocked(__instance, takeWarper))
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
                consumeRegister);

            return false;
        }
    }

    [HarmonyPatch(typeof(StationComponent), "CalcTripEnergyCost")]
    internal static class CalcTripEnergyCostPatch
    {
        private static void Postfix(StationComponent __instance, ref long __result)
        {
            if (!SubspaceLogisticsTechnology.IsUnlocked || !StationSettings.IsEnabled(__instance))
            {
                return;
            }

            int multiplier = WormholeEfficiencyTechnology.EnergyMultiplier;
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

                    // The sending station pays by default; the receiving station pays when the sender has none.
                    if (!StationSettings.TryConsumeSubspaceWarper(source)
                        && !StationSettings.TryConsumeSubspaceWarper(destination))
                    {
                        TransportDiagnostics.Once(
                            source,
                            "no-warper:" + destination.gid,
                            "and station " + TransportDiagnostics.Describe(destination) +
                            " have no Subspace Warpers left for a wormhole transfer.");
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
            TransportDiagnostics.Once(
                source,
                "transfer:" + destination.gid,
                "made its first wormhole transfer to station " + TransportDiagnostics.Describe(destination) +
                " (" + transferredCount + " of item " + itemId + ").");
            return true;
        }

        // Both endpoints must have Subspace Transport enabled, and at least one must hold a Subspace Warper.
        internal static bool CanTransfer(StationComponent source, StationComponent destination, int itemId)
        {
            if (!SubspaceLogisticsTechnology.IsUnlocked
                || source == null
                || destination == null
                || source.gid == destination.gid)
            {
                return false;
            }

            bool sourceEnabled = StationSettings.IsEnabled(source);
            bool destinationEnabled = StationSettings.IsEnabled(destination);
            if (!sourceEnabled && !destinationEnabled)
            {
                return false;
            }

            if (!sourceEnabled || !destinationEnabled)
            {
                StationComponent disabled = sourceEnabled ? destination : source;
                StationComponent enabled = sourceEnabled ? source : destination;
                TransportDiagnostics.Once(
                    enabled,
                    "partner-disabled:" + disabled.gid,
                    "is paired with station " + TransportDiagnostics.Describe(disabled) +
                    ", which does not have Subspace Transport enabled, so they use logistics vessels.");
                return false;
            }

            if (!HasSubspaceWarper(source) && !HasSubspaceWarper(destination))
            {
                TransportDiagnostics.Once(
                    source,
                    "no-warper:" + destination.gid,
                    "and station " + TransportDiagnostics.Describe(destination) +
                    " have no Subspace Warpers in their Subspace Warper slots, so no cargo moves by wormhole.");
                return false;
            }

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

    // Logs each distinct wormhole transport problem once per station, so playtests show why cargo is not moving
    // without flooding the log from the dispatch loop.
    internal static class TransportDiagnostics
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        internal static void Once(StationComponent station, string key, string message)
        {
            string fullKey = station.gid + ":" + key;
            lock (Sync)
            {
                if (!Logged.Add(fullKey))
                {
                    return;
                }
            }

            Plugin.ModLogger?.LogInfo("Subspace Transport: station " + Describe(station) + " " + message);
        }

        // Matches the Control Panel: planet name and the station's "#" number.
        internal static string Describe(StationComponent station)
        {
            PlanetData planet = GameMain.galaxy != null ? GameMain.galaxy.PlanetById(station.planetId) : null;
            string planetName = planet != null ? planet.displayName : "planet " + station.planetId;
            return "#" + station.id + " on " + planetName;
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                Logged.Clear();
            }
        }
    }
}
