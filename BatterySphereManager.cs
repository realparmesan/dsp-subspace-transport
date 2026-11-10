using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;

namespace SubspaceTransport
{
    internal static class BatterySphereManager
    {
        private const int LegacySaveFormatVersion = 1;

        // Version 2 added the planned flag; version 3 moved the accumulator buffers into each sphere.
        private const int PlannedSaveFormatVersion = 2;
        private const int SaveFormatVersion = 3;
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

            // Each sphere keeps its own accumulator stock and stored energy.
            internal int EmptyCount;
            internal int ChargedCount;
            internal long EnergyRemainder;
            internal long PowerTickRemainder;

            internal bool HasStock
            {
                get { return RocketCount > 0 || EmptyCount > 0 || ChargedCount > 0 || EnergyRemainder > 0; }
            }
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, StarProject> Projects = new Dictionary<int, StarProject>();
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
                if (!planned && !project.HasStock)
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
                emptyCount = project != null ? project.EmptyCount : 0;
                chargedCount = project != null ? project.ChargedCount : 0;
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

                foreach (KeyValuePair<int, StarProject> entry in Projects)
                {
                    AdvancePowerUnsafe(entry.Key, entry.Value, tick);
                }

                _lastProcessedTick = tick;

                foreach (KeyValuePair<int, StarProject> entry in Projects)
                {
                    StarProject project = entry.Value;

                    // Already-charged batteries stay deliverable when the sphere is producing no power.
                    if (IsProducing(entry.Key, project))
                    {
                        CollectEmptyAccumulatorsUnsafe(project, stationPool, traffic, shipWarpSpeed);
                        ChargeAccumulatorsUnsafe(project);
                    }

                    SupplyChargedAccumulatorsUnsafe(project, stationPool, traffic, shipWarpSpeed);
                }
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
                    writer.Write(entry.Value.EmptyCount);
                    writer.Write(entry.Value.ChargedCount);
                    writer.Write(entry.Value.EnergyRemainder);
                    writer.Write(entry.Value.PowerTickRemainder);
                }
            }
        }

        internal static void Import(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version < LegacySaveFormatVersion || version > SaveFormatVersion)
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
                bool planned = version >= PlannedSaveFormatVersion ? reader.ReadBoolean() : rocketCount > 0;
                if (starIndex < 0 || rocketCount < 0 || rocketCount > MaximumProjectRockets)
                {
                    throw new InvalidDataException("Invalid Battery Sphere project data.");
                }

                StarProject loaded = new StarProject { RocketCount = rocketCount, Planned = planned };
                if (version >= SaveFormatVersion)
                {
                    ReadBuffers(reader, loaded);
                }

                loadedProjects[starIndex] = loaded;
            }

            if (version < SaveFormatVersion)
            {
                // Older saves had one buffer shared by every sphere; give it to the lowest-numbered sphere.
                StarProject legacy = new StarProject();
                ReadBuffers(reader, legacy);
                int target = -1;
                foreach (int key in loadedProjects.Keys)
                {
                    bool better = target < 0
                        || (loadedProjects[key].Planned && !loadedProjects[target].Planned)
                        || (loadedProjects[key].Planned == loadedProjects[target].Planned && key < target);
                    if (better)
                    {
                        target = key;
                    }
                }

                if (target >= 0)
                {
                    StarProject owner = loadedProjects[target];
                    owner.EmptyCount = legacy.EmptyCount;
                    owner.ChargedCount = legacy.ChargedCount;
                    owner.EnergyRemainder = legacy.EnergyRemainder;
                    owner.PowerTickRemainder = legacy.PowerTickRemainder;
                }
            }

            lock (Sync)
            {
                Projects.Clear();
                foreach (KeyValuePair<int, StarProject> entry in loadedProjects)
                {
                    Projects.Add(entry.Key, entry.Value);
                }

                _lastProcessedTick = -1;
            }
        }

        private static void ReadBuffers(BinaryReader reader, StarProject project)
        {
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

            project.EmptyCount = emptyCount;
            project.ChargedCount = chargedCount;
            project.EnergyRemainder = energyRemainder;
            project.PowerTickRemainder = tickRemainder;
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                Projects.Clear();
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

        private static long GetProjectWattsUnsafe(int starIndex, StarProject project)
        {
            if (!project.Planned || (GameMain.galaxy != null && starIndex >= GameMain.galaxy.starCount))
            {
                return 0L;
            }

            return GetSphereWatts(starIndex);
        }

        private static bool IsProducing(int starIndex, StarProject project)
        {
            return GetProjectWattsUnsafe(starIndex, project) > 0L;
        }

        private static void AdvancePowerUnsafe(int starIndex, StarProject project, long tick)
        {
            if (_lastProcessedTick < 0 || tick <= _lastProcessedTick)
            {
                return;
            }

            long availablePower = GetProjectWattsUnsafe(starIndex, project);
            if (availablePower <= 0 || project.EnergyRemainder >= MaximumBufferedEnergy)
            {
                return;
            }

            long usefulTickLimit =
                (MaximumBufferedEnergy * TicksPerSecond - project.PowerTickRemainder) / availablePower + 1;
            long ticksElapsed = Math.Min(tick - _lastProcessedTick, usefulTickLimit);
            long energyNumerator = availablePower * ticksElapsed + project.PowerTickRemainder;
            long generatedEnergy = energyNumerator / TicksPerSecond;
            project.PowerTickRemainder = energyNumerator % TicksPerSecond;
            project.EnergyRemainder = Math.Min(MaximumBufferedEnergy, project.EnergyRemainder + generatedEnergy);
        }

        private static void CollectEmptyAccumulatorsUnsafe(
            StarProject project,
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
                            || project.EmptyCount >= MaximumBatteryBuffer)
                        {
                            continue;
                        }

                        // Like a vessel, a pickup must carry more than the minimum load. The sphere's own buffer
                        // can cap it too, so a nearly full buffer waits instead of taking a few at a time.
                        int minimumLoad = GetMinimumLoad(station, store.max);
                        int supplyCount = Math.Min(store.count, store.remoteSupplyCount);
                        int transferCount = Math.Min(
                            GetMaximumTransfer(),
                            Math.Min(supplyCount, MaximumBatteryBuffer - project.EmptyCount));
                        if (transferCount <= minimumLoad)
                        {
                            continue;
                        }

                        long energyCost = WormholeEnergy.Multiply(station.CalcTripEnergyCost(0.0, shipWarpSpeed, true));
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
                        project.EmptyCount += transferCount;
                        traffic?.RegisterPlanetOutputStat(station.planetId, ItemProto.kAccumulator, transferCount);
                    }
                }
            }
        }

        private static void ChargeAccumulatorsUnsafe(StarProject project)
        {
            if (project.EmptyCount <= 0 || project.ChargedCount >= MaximumBatteryBuffer)
            {
                return;
            }

            int chargeCount = (int)Math.Min(
                Math.Min(project.EmptyCount, MaximumBatteryBuffer - project.ChargedCount),
                project.EnergyRemainder / BatteryEnergyJoules);
            if (chargeCount <= 0)
            {
                return;
            }

            project.EmptyCount -= chargeCount;
            project.ChargedCount += chargeCount;
            project.EnergyRemainder -= (long)chargeCount * BatteryEnergyJoules;
        }

        private static void SupplyChargedAccumulatorsUnsafe(
            StarProject project,
            StationComponent[] stationPool,
            TrafficStatistics traffic,
            float shipWarpSpeed)
        {
            for (int stationIndex = 1; stationIndex < stationPool.Length; stationIndex++)
            {
                StationComponent station = stationPool[stationIndex];
                if (project.ChargedCount <= 0)
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
                            || project.ChargedCount <= 0)
                        {
                            continue;
                        }

                        int demandCount = Math.Min(store.remoteDemandCount, project.ChargedCount);
                        int minimumLoad = GetMinimumLoad(station, store.max);
                        if (demandCount <= minimumLoad)
                        {
                            TransportDiagnostics.Once(
                                station,
                                "battery-minload",
                                "wants charged batteries but demand " + demandCount + " (remote demand " +
                                store.remoteDemandCount + ", pool " + project.ChargedCount +
                                ") does not exceed its minimum load " + minimumLoad + ".");
                            continue;
                        }

                        int transferCount = Math.Min(GetMaximumTransfer(), demandCount);
                        long energyCost = WormholeEnergy.Multiply(station.CalcTripEnergyCost(0.0, shipWarpSpeed, true));
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
                        project.ChargedCount -= transferCount;
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
