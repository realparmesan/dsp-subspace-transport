using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    internal static class BatterySphereManager
    {
        private const int SaveFormatVersion = 1;
        private const int MaximumSavedStars = 10000;
        private const int MaximumProjectRockets = 1000000;
        private const int MaximumBatteryBuffer = 100000;
        private const int MaximumTransferCount = 1000;
        private const int BatteryEnergyJoules = 270000000;
        private const long MaximumBufferedEnergy = (long)BatteryEnergyJoules * MaximumBatteryBuffer;
        private const long PowerPerRocketWatts = 1000000;
        private const long TicksPerSecond = 60;

        private sealed class StarProject
        {
            internal int RocketCount;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, StarProject> Projects = new Dictionary<int, StarProject>();
        private static int _emptyAccumulatorCount;
        private static int _chargedAccumulatorCount;
        private static long _energyRemainder;
        private static long _powerTickRemainder;
        private static long _lastProcessedTick = -1;

        internal static void RegisterRocket(int starIndex)
        {
            if (starIndex < 0)
            {
                Plugin.ModLogger.LogWarning("Ignored a Battery Sphere Rocket launched without a valid star.");
                return;
            }

            lock (Sync)
            {
                StarProject project;
                if (!Projects.TryGetValue(starIndex, out project))
                {
                    project = new StarProject();
                    Projects.Add(starIndex, project);
                }

                if (project.RocketCount < MaximumProjectRockets)
                {
                    project.RocketCount++;
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
                outputWatts = rocketCount * PowerPerRocketWatts;
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
                || SubspaceLogisticsTechnology.CurrentLevel <= 0
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

                if (GetTotalOutputWattsUnsafe() <= 0L)
                {
                    return;
                }

                CollectEmptyAccumulatorsUnsafe(stationPool, traffic, shipWarpSpeed);
                ChargeAccumulatorsUnsafe();
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
            if (version != SaveFormatVersion)
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
                if (starIndex < 0 || rocketCount < 0 || rocketCount > MaximumProjectRockets)
                {
                    throw new InvalidDataException("Invalid Battery Sphere project data.");
                }

                loadedProjects[starIndex] = new StarProject { RocketCount = rocketCount };
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

        private static long GetTotalOutputWattsUnsafe()
        {
            long rocketCount = 0;
            foreach (KeyValuePair<int, StarProject> entry in Projects)
            {
                if (GameMain.galaxy != null
                    && entry.Key >= GameMain.galaxy.starCount)
                {
                    continue;
                }

                rocketCount += entry.Value.RocketCount;
            }

            return rocketCount * PowerPerRocketWatts;
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

                        int supplyCount = Math.Min(store.count, store.remoteSupplyCount);
                        int transferCount = Math.Min(
                            MaximumTransferCount,
                            Math.Min(supplyCount, MaximumBatteryBuffer - _emptyAccumulatorCount));
                        int moduleIndex = FindHyperdriveModule(station.storage);
                        long energyCost = station.CalcTripEnergyCost(0.0, shipWarpSpeed, true);
                        if (transferCount <= 0 || moduleIndex < 0 || station.energy < energyCost)
                        {
                            continue;
                        }

                        int transferredInc = SplitInc(store.count, store.inc, transferCount);
                        store.count -= transferCount;
                        store.inc -= transferredInc;
                        station.storage[storageIndex] = store;
                        ConsumeModule(station.storage, moduleIndex);
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
                if (!CanUseStation(station) || _chargedAccumulatorCount <= 0)
                {
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

                        int transferCount = Math.Min(
                            MaximumTransferCount,
                            Math.Min(store.remoteDemandCount, _chargedAccumulatorCount));
                        int moduleIndex = FindHyperdriveModule(station.storage);
                        long energyCost = station.CalcTripEnergyCost(0.0, shipWarpSpeed, true);
                        if (transferCount <= 0 || moduleIndex < 0 || station.energy < energyCost)
                        {
                            continue;
                        }

                        store.count += transferCount;
                        station.storage[storageIndex] = store;
                        ConsumeModule(station.storage, moduleIndex);
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

        private static bool CanUseStation(StationComponent station)
        {
            return station != null
                && station.id > 0
                && station.gid > 0
                && station.isStellar
                && station.storage != null
                && StationSettings.IsEnabled(station)
                && InstantTransport.HasHyperdriveModule(station);
        }

        private static int FindHyperdriveModule(StationStore[] storage)
        {
            for (int i = 0; i < storage.Length; i++)
            {
                if (storage[i].itemId == HyperdriveModuleContent.ItemId && storage[i].count > 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void ConsumeModule(StationStore[] storage, int moduleIndex)
        {
            StationStore module = storage[moduleIndex];
            module.count--;
            module.inc -= SplitInc(module.count + 1, module.inc, 1);
            storage[moduleIndex] = module;
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
        [ThreadStatic]
        private static bool _batteryRocketLaunch;

        [ThreadStatic]
        private static int _launchStarIndex;

        private static void Prefix(
            SiloComponent __instance,
            ref DysonSphere sphere,
            out bool __state)
        {
            __state = _batteryRocketLaunch;
            _batteryRocketLaunch = __instance.bulletId == BatterySphereContent.ItemId
                && BatterySphereContent.IsUnlocked;
            if (!_batteryRocketLaunch)
            {
                return;
            }

            _launchStarIndex = __instance.planetId / 100;
            if (sphere == null && GameMain.data != null && GameMain.galaxy != null
                && _launchStarIndex >= 0
                && _launchStarIndex < GameMain.galaxy.starCount)
            {
                sphere = GameMain.data.CreateDysonSphere(_launchStarIndex);
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            _batteryRocketLaunch = __state;
            if (!__state)
            {
                _launchStarIndex = 0;
            }

            return __exception;
        }

        internal static bool IsCapturingRocket
        {
            get { return _batteryRocketLaunch; }
        }

        internal static int LaunchStarIndex
        {
            get { return _launchStarIndex; }
        }
    }

    [HarmonyPatch(typeof(DysonSphere), "GetAutoNodeCount")]
    internal static class BatterySphereAutoNodePatch
    {
        private static void Postfix(ref int __result)
        {
            if (BatterySphereSiloLaunchPatch.IsCapturingRocket && __result == 0)
            {
                __result = 1;
            }
        }
    }

    [HarmonyPatch(typeof(DysonSphere), "AddDysonRocket")]
    internal static class BatterySphereRocketCapturePatch
    {
        private static bool Prefix(DysonSphere __instance)
        {
            if (!BatterySphereSiloLaunchPatch.IsCapturingRocket)
            {
                return true;
            }

            int starIndex = __instance.starData != null
                ? __instance.starData.index
                : BatterySphereSiloLaunchPatch.LaunchStarIndex;
            BatterySphereManager.RegisterRocket(starIndex);
            return false;
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

    [HarmonyPatch(typeof(UIDysonEditor), "_OnUpdate")]
    internal static class BatterySphereProjectUIPatch
    {
        private const string StatusObjectName = "wormhole-battery-sphere-status";
        private static UIDysonEditor _editor;
        private static Text _status;
        private static string _lastText;

        private static void Postfix(UIDysonEditor __instance)
        {
            EnsureStatus(__instance);
            if (_status == null)
            {
                return;
            }

            StarData star = __instance.selection != null ? __instance.selection.viewStar : null;
            _status.gameObject.SetActive(star != null);
            if (star == null)
            {
                return;
            }

            string text;
            if (!BatterySphereContent.IsUnlocked)
            {
                text = "Battery Sphere\nResearch Battery Sphere Engineering to unlock.";
            }
            else
            {
                int rockets;
                long power;
                int empty;
                int charged;
                BatterySphereManager.GetProjectStatus(star.index, out rockets, out power, out empty, out charged);
                text = string.Format(
                    "Battery Sphere Project\nRockets: {0:N0}\nDedicated output: {1:N0} MW\nAccumulator buffer: {2:N0} empty / {3:N0} charged",
                    rockets,
                    power / 1000000L,
                    empty,
                    charged);
            }

            if (_lastText != text)
            {
                _status.text = text;
                _lastText = text;
            }
        }

        private static void EnsureStatus(UIDysonEditor editor)
        {
            if (_status != null && _editor == editor)
            {
                return;
            }

            Transform existing = editor.transform.Find(StatusObjectName);
            GameObject statusObject;
            if (existing != null)
            {
                statusObject = existing.gameObject;
            }
            else
            {
                statusObject = new GameObject(StatusObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                statusObject.transform.SetParent(editor.transform, false);
            }

            RectTransform rect = statusObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(370f, 112f);

            _status = statusObject.GetComponent<Text>();
            _status.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _status.fontSize = 15;
            _status.color = Color.white;
            _status.alignment = TextAnchor.UpperLeft;
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Overflow;
            _status.raycastTarget = false;
            _editor = editor;
            _lastText = null;
        }
    }
}
