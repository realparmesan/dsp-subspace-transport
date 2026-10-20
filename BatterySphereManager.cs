using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;

namespace SubspaceTransport
{
    internal static class BatterySphereManager
    {
        private const int LegacySaveFormatVersion = 1;
        private const int SaveFormatVersion = 2;
        private const int MaximumSavedStars = 10000;
        private const int MaximumProjectRockets = 1000000;
        private const int MaximumBatteryBuffer = 100000;
        private const int MaximumTransferCount = 1000;
        private const int BatteryEnergyJoules = 270000000;
        private const long MaximumBufferedEnergy = (long)BatteryEnergyJoules * MaximumBatteryBuffer;
        private const long TicksPerSecond = 60;

        private sealed class StarProject
        {
            internal int RocketCount;

            // Set from the Dyson Sphere editor. Rockets only launch to, and output only counts for, planned stars;
            // switching a star back to a Dyson Sphere keeps its rockets for when it is planned again.
            internal bool Planned;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, StarProject> Projects = new Dictionary<int, StarProject>();
        private static int _emptyAccumulatorCount;
        private static int _chargedAccumulatorCount;
        private static long _energyRemainder;
        private static long _powerTickRemainder;
        private static long _lastProcessedTick = -1;

        internal static bool IsPlanned(int starIndex)
        {
            lock (Sync)
            {
                StarProject project;
                return Projects.TryGetValue(starIndex, out project) && project.Planned;
            }
        }

        internal static void SetPlanned(int starIndex, bool planned)
        {
            if (starIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(starIndex));
            }

            lock (Sync)
            {
                StarProject project;
                if (!Projects.TryGetValue(starIndex, out project))
                {
                    if (!planned)
                    {
                        return;
                    }

                    project = new StarProject();
                    Projects.Add(starIndex, project);
                }

                project.Planned = planned;
                if (!planned && project.RocketCount == 0)
                {
                    Projects.Remove(starIndex);
                }
            }
        }

        internal static void GetProjectStatus(
            int starIndex,
            out int rocketCount,
            out long outputWatts,
            out int emptyCount,
            out int chargedCount)
        {
            lock (Sync)
            {
                StarProject project;
                rocketCount = Projects.TryGetValue(starIndex, out project)
                    ? project.RocketCount
                    : 0;
                outputWatts = GetSphereWatts(starIndex);
                emptyCount = _emptyAccumulatorCount;
                chargedCount = _chargedAccumulatorCount;
            }
        }

        internal static void ProcessStationPool(
            StationComponent[] stationPool,
            TrafficStatistics traffic,
            float shipWarpSpeed)
        {
            if (stationPool == null
                || !SubspaceLogisticsTechnology.IsUnlocked
                || !BatterySphereContent.IsUnlocked
                || GameMain.instance == null)
            {
                return;
            }

            lock (Sync)
            {
                long tick = GameMain.gameTick;
                if (_lastProcessedTick == tick)
                {
                    return;
                }

                AdvancePowerUnsafe(tick);
                _lastProcessedTick = tick;

                // Already-charged batteries stay deliverable when the sphere is producing no power.
                if (GetTotalOutputWattsUnsafe() > 0L)
                {
                    CollectEmptyAccumulatorsUnsafe(stationPool, traffic, shipWarpSpeed);
                    ChargeAccumulatorsUnsafe();
                }

                SupplyChargedAccumulatorsUnsafe(stationPool, traffic, shipWarpSpeed);
            }
        }

        internal static void Export(BinaryWriter writer)
        {
            lock (Sync)
            {
                writer.Write(SaveFormatVersion);
                writer.Write(Projects.Count);
                foreach (KeyValuePair<int, StarProject> entry in Projects)
                {
                    writer.Write(entry.Key);
                    writer.Write(entry.Value.RocketCount);
                    writer.Write(entry.Value.Planned);
                }

                writer.Write(_emptyAccumulatorCount);
                writer.Write(_chargedAccumulatorCount);
                writer.Write(_energyRemainder);
                writer.Write(_powerTickRemainder);
            }
        }

        internal static void Import(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version != SaveFormatVersion && version != LegacySaveFormatVersion)
            {
                throw new InvalidDataException("Unsupported Battery Sphere save format: " + version + ".");
            }

            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumSavedStars)
            {
                throw new InvalidDataException("Invalid Battery Sphere project count: " + count + ".");
            }

            Dictionary<int, StarProject> loadedProjects = new Dictionary<int, StarProject>();
            for (int i = 0; i < count; i++)
            {
                int starIndex = reader.ReadInt32();
                int rocketCount = reader.ReadInt32();

                // Saves from before sphere planning existed treat every star with rockets as planned.
                bool planned = version >= SaveFormatVersion ? reader.ReadBoolean() : rocketCount > 0;
                if (starIndex < 0 || rocketCount < 0 || rocketCount > MaximumProjectRockets)
                {
                    throw new InvalidDataException("Invalid Battery Sphere project data.");
                }

                loadedProjects[starIndex] = new StarProject { RocketCount = rocketCount, Planned = planned };
            }

            int emptyCount = reader.ReadInt32();
            int chargedCount = reader.ReadInt32();
            long energyRemainder = reader.ReadInt64();
            long tickRemainder = reader.ReadInt64();
            if (emptyCount < 0 || emptyCount > MaximumBatteryBuffer
                || chargedCount < 0 || chargedCount > MaximumBatteryBuffer
                || energyRemainder < 0 || energyRemainder > MaximumBufferedEnergy
                || tickRemainder < 0 || tickRemainder >= TicksPerSecond)
            {
                throw new InvalidDataException("Invalid Battery Sphere accumulator buffer data.");
            }

            lock (Sync)
            {
                Projects.Clear();
                foreach (KeyValuePair<int, StarProject> entry in loadedProjects)
                {
                    Projects.Add(entry.Key, entry.Value);
                }

                _emptyAccumulatorCount = emptyCount;
                _chargedAccumulatorCount = chargedCount;
                _energyRemainder = energyRemainder;
                _powerTickRemainder = tickRemainder;
                _lastProcessedTick = -1;
            }
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                Projects.Clear();
                _emptyAccumulatorCount = 0;
                _chargedAccumulatorCount = 0;
                _energyRemainder = 0;
                _powerTickRemainder = 0;
                _lastProcessedTick = -1;
            }
        }

        // A Battery Sphere is an ordinary Dyson Sphere built by Battery Rockets; its generated power charges
        // accumulators instead of reaching the grid.
        private static long GetSphereWatts(int starIndex)
        {
            DysonSphere[] spheres = GameMain.data != null ? GameMain.data.dysonSpheres : null;
            DysonSphere sphere = spheres != null && starIndex >= 0 && starIndex < spheres.Length
                ? spheres[starIndex]
                : null;
            return sphere != null ? sphere.energyGenCurrentTick * TicksPerSecond : 0L;
        }

        private static long GetTotalOutputWattsUnsafe()
        {
            long watts = 0;
            foreach (KeyValuePair<int, StarProject> entry in Projects)
            {
                if (!entry.Value.Planned
                    || (GameMain.galaxy != null && entry.Key >= GameMain.galaxy.starCount))
                {
                    continue;
                }

                watts += GetSphereWatts(entry.Key);
            }

            return watts;
        }

        private static void AdvancePowerUnsafe(long tick)
        {
            if (_lastProcessedTick < 0 || tick <= _lastProcessedTick)
            {
                return;
            }

            long availablePower = GetTotalOutputWattsUnsafe();
            if (availablePower <= 0 || _energyRemainder >= MaximumBufferedEnergy)
            {
                return;
            }

            long usefulTickLimit =
                (MaximumBufferedEnergy * TicksPerSecond - _powerTickRemainder) / availablePower + 1;
            long ticksElapsed = Math.Min(tick - _lastProcessedTick, usefulTickLimit);
            long energyNumerator = availablePower * ticksElapsed + _powerTickRemainder;
            long generatedEnergy = energyNumerator / TicksPerSecond;
            _powerTickRemainder = energyNumerator % TicksPerSecond;
            _energyRemainder = Math.Min(MaximumBufferedEnergy, _energyRemainder + generatedEnergy);
        }

        private static void CollectEmptyAccumulatorsUnsafe(
            StationComponent[] stationPool,
            TrafficStatistics traffic,
            float shipWarpSpeed)
        {
            for (int stationIndex = 1; stationIndex < stationPool.Length; stationIndex++)
            {
                StationComponent station = stationPool[stationIndex];
                if (!CanUseStation(station))
                {
                    continue;
                }

                lock (station.storage)
                {
                    for (int storageIndex = 0; storageIndex < station.storage.Length; storageIndex++)
                    {
                        StationStore store = station.storage[storageIndex];
                        if (store.itemId != ItemProto.kAccumulator
                            || store.remoteLogic != ELogisticStorage.Supply
                            || store.count <= 0
                            || _emptyAccumulatorCount >= MaximumBatteryBuffer)
                        {
                            continue;
                        }

                        // Like a vessel, a pickup must carry more than the minimum load. The sphere's own buffer
                        // can cap it too, so a nearly full buffer waits instead of taking a few at a time.
                        int minimumLoad = GetMinimumLoad(station, store.max);
                        int supplyCount = Math.Min(store.count, store.remoteSupplyCount);
                        int transferCount = Math.Min(
                            GetMaximumTransfer(),
                            Math.Min(supplyCount, MaximumBatteryBuffer - _emptyAccumulatorCount));
                        if (transferCount <= minimumLoad)
                        {
                            continue;
                        }

                        long energyCost = station.CalcTripEnergyCost(0.0, shipWarpSpeed, true);
                        if (transferCount <= 0
                            || station.energy < energyCost
                            || !StationSettings.TryConsumeSubspaceWarper(station))
                        {
                            continue;
                        }

                        int transferredInc = SplitInc(store.count, store.inc, transferCount);
                        store.count -= transferCount;
                        store.inc -= transferredInc;
                        station.storage[storageIndex] = store;
                        station.energy -= energyCost;
                        _emptyAccumulatorCount += transferCount;
                        traffic?.RegisterPlanetOutputStat(station.planetId, ItemProto.kAccumulator, transferCount);
                    }
                }
            }
        }

        private static void ChargeAccumulatorsUnsafe()
        {
            if (_emptyAccumulatorCount <= 0 || _chargedAccumulatorCount >= MaximumBatteryBuffer)
            {
                return;
            }

            int chargeCount = (int)Math.Min(
                Math.Min(_emptyAccumulatorCount, MaximumBatteryBuffer - _chargedAccumulatorCount),
                _energyRemainder / BatteryEnergyJoules);
            if (chargeCount <= 0)
            {
                return;
            }

            _emptyAccumulatorCount -= chargeCount;
            _chargedAccumulatorCount += chargeCount;
            _energyRemainder -= (long)chargeCount * BatteryEnergyJoules;
        }

        private static void SupplyChargedAccumulatorsUnsafe(
            StationComponent[] stationPool,
            TrafficStatistics traffic,
            float shipWarpSpeed)
        {
            for (int stationIndex = 1; stationIndex < stationPool.Length; stationIndex++)
            {
                StationComponent station = stationPool[stationIndex];
                if (_chargedAccumulatorCount <= 0)
                {
                    continue;
                }

                if (!CanUseStation(station))
                {
                    LogUnusableDemandStation(station);
                    continue;
                }

                lock (station.storage)
                {
                    for (int storageIndex = 0; storageIndex < station.storage.Length; storageIndex++)
                    {
                        StationStore store = station.storage[storageIndex];
                        if (store.itemId != ItemProto.kAccumulatorFull
                            || store.remoteLogic != ELogisticStorage.Demand
                            || store.remoteDemandCount <= 0
                            || _chargedAccumulatorCount <= 0)
                        {
                            continue;
                        }

                        int demandCount = Math.Min(store.remoteDemandCount, _chargedAccumulatorCount);
                        int minimumLoad = GetMinimumLoad(station, store.max);
                        if (demandCount <= minimumLoad)
                        {
                            TransportDiagnostics.Once(
                                station,
                                "battery-minload",
                                "wants charged batteries but demand " + demandCount + " (remote demand " +
                                store.remoteDemandCount + ", pool " + _chargedAccumulatorCount +
                                ") does not exceed its minimum load " + minimumLoad + ".");
                            continue;
                        }

                        int transferCount = Math.Min(GetMaximumTransfer(), demandCount);
                        long energyCost = station.CalcTripEnergyCost(0.0, shipWarpSpeed, true);
                        if (transferCount <= 0
                            || station.energy < energyCost
                            || !StationSettings.TryConsumeSubspaceWarper(station))
                        {
                            TransportDiagnostics.Once(
                                station,
                                "battery-supply-blocked",
                                "cannot receive charged batteries: energy " + station.energy + " vs cost " +
                                energyCost + ", or no Subspace Warper to consume.");
                            continue;
                        }

                        store.count += transferCount;
                        station.storage[storageIndex] = store;
                        station.energy -= energyCost;
                        _chargedAccumulatorCount -= transferCount;
                        traffic?.RegisterPlanetInputStat(
                            station.planetId,
                            ItemProto.kAccumulatorFull,
                            transferCount);
                    }
                }
            }
        }

        // The station's minimum vessel load setting, as vessel dispatch applies it: a transfer must carry more
        // than this many items, but never more than the slot can hold.
        private static void LogUnusableDemandStation(StationComponent station)
        {
            if (station == null || station.storage == null)
            {
                return;
            }

            for (int i = 0; i < station.storage.Length; i++)
            {
                StationStore store = station.storage[i];
                if (store.itemId == ItemProto.kAccumulatorFull && store.remoteLogic == ELogisticStorage.Demand)
                {
                    TransportDiagnostics.Once(
                        station,
                        "battery-unusable",
                        "demands charged batteries but cannot use the Battery Sphere: stellar=" +
                        station.isStellar + ", enabled=" + StationSettings.IsEnabled(station) +
                        ", warpers=" + StationSettings.GetSubspaceWarperCount(station) + ".");
                    return;
                }
            }
        }

        private static int GetMinimumLoad(StationComponent station, int slotMax)
        {
            int carries = GameMain.history != null ? GameMain.history.logisticShipCarries : 1;
            int threshold = (carries - 1) * station.deliveryShips / 100;
            return Math.Max(0, Math.Min(threshold, slotMax - 1));
        }

        private static int GetMaximumTransfer()
        {
            int carries = GameMain.history != null ? GameMain.history.logisticShipCarries : 0;
            return Math.Max(MaximumTransferCount, carries);
        }

        private static bool CanUseStation(StationComponent station)
        {
            return station != null
                && station.id > 0
                && station.gid > 0
                && station.isStellar
                && station.storage != null
                && StationSettings.IsEnabled(station)
                && InstantTransport.HasSubspaceWarper(station);
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
    }

    [HarmonyPatch(typeof(SiloComponent), "InternalUpdate")]
    internal static class BatterySphereSiloLaunchPatch
    {
        private enum LaunchMode
        {
            Vanilla,
            Capture,
            Block
        }

        [ThreadStatic]
        private static LaunchMode _mode;

        [ThreadStatic]
        private static int _launchStarIndex;

        private struct SiloState
        {
            internal LaunchMode Mode;
            internal int StarIndex;
        }

        private static void Prefix(
            SiloComponent __instance,
            ref DysonSphere sphere,
            out SiloState __state)
        {
            __state = new SiloState { Mode = _mode, StarIndex = _launchStarIndex };
            _mode = LaunchMode.Vanilla;
            bool battery = __instance.bulletId == BatterySphereContent.ItemId;
            bool hyperMass = __instance.bulletId == HyperMassContent.ItemId;

            // planetId is starId * 100 + planet number, and starId is the star index + 1.
            _launchStarIndex = __instance.planetId / 100 - 1;
            bool validStar = GameMain.galaxy != null
                && _launchStarIndex >= 0
                && _launchStarIndex < GameMain.galaxy.starCount;
            if (!battery && !hyperMass)
            {
                // Only Hyper Mass Rockets build a Hyper Mass Injector; Carrier Rockets wait in the silo.
                if (validStar && HyperMassManager.IsPlanned(_launchStarIndex))
                {
                    _mode = LaunchMode.Block;
                }

                return;
            }

            bool planned = battery
                ? BatterySphereContent.IsUnlocked && BatterySphereManager.IsPlanned(_launchStarIndex)
                : HyperMassContent.IsUnlocked && HyperMassManager.IsPlanned(_launchStarIndex);
            if (!validStar || !planned)
            {
                // Project rockets wait in the silo until their sphere type is planned at this star.
                _mode = LaunchMode.Block;
                return;
            }

            // A planned star builds with project rockets exactly like a Dyson Sphere does with Carrier Rockets.
            _mode = LaunchMode.Capture;
            if (sphere == null && GameMain.data != null)
            {
                sphere = GameMain.data.CreateDysonSphere(_launchStarIndex);
            }
        }

        private static Exception Finalizer(Exception __exception, SiloState __state)
        {
            _mode = __state.Mode;
            _launchStarIndex = __state.StarIndex;
            return __exception;
        }


        internal static bool IsBlockingRocket
        {
            get { return _mode == LaunchMode.Block; }
        }
    }

    [HarmonyPatch(typeof(DysonSphere), "GetAutoNodeCount")]
    internal static class BatterySphereAutoNodePatch
    {
        private static void Postfix(ref int __result)
        {
            if (BatterySphereSiloLaunchPatch.IsBlockingRocket)
            {
                __result = 0;
            }
        }
    }

    [HarmonyPatch(typeof(StationComponent), "DetermineDispatch")]
    internal static class BatterySphereStationExchangePatch
    {
        private static void Postfix(
            float shipWarpSpeed,
            StationComponent[] gStationPool,
            TrafficStatistics tstat)
        {
            BatterySphereManager.ProcessStationPool(gStationPool, tstat, shipWarpSpeed);
        }
    }
}
