using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;

namespace SubspaceTransport
{
    // Subspace Drones housed in Subspace Restockers anywhere in the cluster restock Icarus' delivery package.
    //
    // A delivery package slot's "at least" setting is the request level and its "at most" setting the maximum: once
    // Icarus holds fewer than the request level (inventory, hand and slot together, like vanilla counts it), an idle
    // drone takes up to the maximum from a restocker and delivers it after a fixed trip. A drone carries 100 times
    // a Logistics Bot's load, and each trip uses one Subspace Warper from the restocker it leaves.
    //
    // Restockers are planetary logistics stations. Their drone and Subspace Warper counts live in StationSettings.
    internal static class SubspaceRestockerManager
    {
        internal const int CarryMultiplier = 100;
        internal const int TripTicks = 300;

        private const int DispatchIntervalTicks = 30;
        private const int RescanIntervalTicks = 600;
        private const int SaveFormatVersion = 1;
        private const int MaximumSavedTrips = 100000;

        private sealed class Trip
        {
            internal long RestockerKey;
            internal int ItemId;
            internal int Count;
            internal int Inc;
            internal int GridIndex;
            internal int RemainingTicks;
        }

        private struct Restocker
        {
            internal PlanetFactory Factory;
            internal StationComponent Station;
            internal long Key;
        }

        private static readonly List<Trip> Trips = new List<Trip>();
        private static readonly List<Restocker> Restockers = new List<Restocker>();
        private static long _lastScanTick = -1;

        // Every restocker station and its key (planet << 32 | entity). Logistics worker threads read it, so it is
        // replaced rather than changed. Station objects are pooled and reused, so membership is by reference and is
        // updated whenever a station is created or removed.
        private static volatile Dictionary<StationComponent, long> _registry = new Dictionary<StationComponent, long>();

        internal static void GameTick(Player player, long time)
        {
            if (player == null || player != GameMain.mainPlayer)
            {
                return;
            }

            AdvanceTrips(player);
            SubspaceDroneAnimation.GameTick();

            // The registry the station patches read is rebuilt here, so a loaded save's restockers work at once.
            if (_lastScanTick < 0 || time - _lastScanTick >= RescanIntervalTicks || time < _lastScanTick)
            {
                Rescan();
                _lastScanTick = time;
            }

            if (time % DispatchIntervalTicks == 0 && SubspaceRestockerContent.IsUnlocked)
            {
                Dispatch(player);
            }
        }

        internal static bool IsRestocker(StationComponent station)
        {
            return station != null && _registry.ContainsKey(station);
        }

        internal static bool TryGetKey(StationComponent station, out long key)
        {
            key = 0;
            return station != null && _registry.TryGetValue(station, out key);
        }

        private static bool IsRestockerEntity(PlanetFactory factory, StationComponent station)
        {
            return factory?.entityPool != null
                && station != null
                && station.id > 0
                && station.entityId > 0
                && station.entityId < factory.entityPool.Length
                && factory.entityPool[station.entityId].stationId == station.id
                && factory.entityPool[station.entityId].protoId == SubspaceRestockerContent.RestockerItemId;
        }

        // A recycled station object may have been a restocker, so every new station is checked.
        internal static void OnStationCreated(PlanetTransport transport, StationComponent station)
        {
            PlanetFactory factory = transport?.factory;
            if (station == null || factory == null)
            {
                return;
            }

            bool restocker = IsRestockerEntity(factory, station);
            if (restocker == IsRestocker(station))
            {
                return;
            }

            Dictionary<StationComponent, long> registry = new Dictionary<StationComponent, long>(_registry);
            if (restocker)
            {
                registry[station] = GetKey(factory, station);
                _lastScanTick = -1;
            }
            else
            {
                registry.Remove(station);
            }

            _registry = registry;
        }

        internal static void OnStationRemoved(StationComponent station)
        {
            if (!IsRestocker(station))
            {
                return;
            }

            Dictionary<StationComponent, long> registry = new Dictionary<StationComponent, long>(_registry);
            registry.Remove(station);
            _registry = registry;
        }

        internal static int CountBusyDrones(StationComponent station)
        {
            long key;
            if (!TryGetKey(station, out key))
            {
                return 0;
            }

            int busy = 0;
            foreach (Trip trip in Trips)
            {
                if (trip.RestockerKey == key)
                {
                    busy++;
                }
            }

            return busy;
        }

        internal static int DroneCarryCapacity
        {
            get
            {
                GameHistoryData history = GameMain.history;
                int carries = history != null ? Math.Max(1, history.logisticCourierCarries) : 1;
                return (int)Math.Min(int.MaxValue, (long)carries * CarryMultiplier);
            }
        }

        // Also rebuilds the registry, which is how restockers loaded from a save are found.
        private static void Rescan()
        {
            Restockers.Clear();
            Dictionary<StationComponent, long> registry = new Dictionary<StationComponent, long>();
            GameData data = GameMain.data;
            for (int f = 0; data?.factories != null && f < data.factoryCount && f < data.factories.Length; f++)
            {
                PlanetFactory factory = data.factories[f];
                StationComponent[] pool = factory?.transport?.stationPool;
                if (pool == null || factory.planet == null)
                {
                    continue;
                }

                int cursor = Math.Min(factory.transport.stationCursor, pool.Length);
                for (int i = 1; i < cursor; i++)
                {
                    StationComponent station = pool[i];
                    if (station == null || station.id != i || !IsRestockerEntity(factory, station))
                    {
                        continue;
                    }

                    long key = GetKey(factory, station);
                    registry[station] = key;
                    Restockers.Add(new Restocker
                    {
                        Factory = factory,
                        Station = station,
                        Key = key
                    });
                }
            }

            _registry = registry;
        }

        private static void Dispatch(Player player)
        {
            DeliveryPackage package = player.deliveryPackage;
            if (package == null || !package.unlockedAndEnabled || package.grids == null || Restockers.Count == 0)
            {
                return;
            }

            int carry = DroneCarryCapacity;
            for (int index = 0; index < package.grids.Length; index++)
            {
                if (!package.IsGridActive(index))
                {
                    continue;
                }

                DeliveryPackage.GRID grid = package.grids[index];
                int itemId = grid.itemId;
                int request = grid.clampedRequireCount;
                if (itemId <= 0
                    || request <= 0
                    || itemId == SubspaceWarperContent.ItemId
                    || itemId == SubspaceRestockerContent.DroneItemId)
                {
                    continue;
                }

                long held = (long)player.packageUtility.GetPackageItemCountIncludeHandItem(itemId)
                    + grid.count
                    + CountInFlight(itemId);
                if (held >= request)
                {
                    continue;
                }

                // "No recycling" leaves the maximum unset, so the request level is the target as well.
                long target = grid.recycleCount == int.MaxValue ? request : Math.Max(request, grid.recycleCount);
                // Capped by the room Icarus has, so a full inventory doesn't send drones back and forth.
                long need = Math.Min(target - held, GetFreeSpace(player, grid, itemId) - CountInFlight(itemId));
                while (need > 0)
                {
                    Restocker source;
                    int stock;
                    if (!FindSource(itemId, out source, out stock))
                    {
                        break;
                    }

                    int amount = (int)Math.Min(Math.Min(need, carry), stock);
                    if (!TryConsumeWarper(source.Station))
                    {
                        break;
                    }

                    int inc;
                    int taken = TakeItem(source.Station, itemId, amount, out inc);
                    if (taken <= 0)
                    {
                        StationSettings.AddSubspaceWarpers(source.Station, 1);
                        break;
                    }

                    Trips.Add(new Trip
                    {
                        RestockerKey = source.Key,
                        ItemId = itemId,
                        Count = taken,
                        Inc = inc,
                        GridIndex = index,
                        RemainingTicks = TripTicks
                    });
                    SubspaceDroneAnimation.Depart(source.Factory, source.Station.entityId, itemId, taken);
                    need -= taken;
                }
            }
        }

        // The restocker with the most of the item that has an idle drone and a Subspace Warper, so a top-up takes
        // as few trips as possible.
        private static bool FindSource(int itemId, out Restocker best, out int bestStock)
        {
            best = default(Restocker);
            bestStock = 0;
            for (int i = Restockers.Count - 1; i >= 0; i--)
            {
                Restocker restocker = Restockers[i];
                StationComponent station = restocker.Station;
                if (!IsRestocker(station) || !IsRestockerEntity(restocker.Factory, station))
                {
                    Restockers.RemoveAt(i);
                    continue;
                }

                int stock = CountItem(station, itemId);
                if (stock <= bestStock
                    || (StationSettings.GetSubspaceWarperCount(station) <= 0
                        && CountItem(station, SubspaceWarperContent.ItemId) <= 0)
                    || StationSettings.GetSubspaceDroneCount(station) <= CountBusyDrones(station))
                {
                    continue;
                }

                best = restocker;
                bestStock = stock;
            }

            return bestStock > 0;
        }

        private static int CountItem(StationComponent station, int itemId)
        {
            StationStore[] storage = station.storage;
            long count = 0;
            lock (storage)
            {
                for (int i = 0; i < storage.Length; i++)
                {
                    if (storage[i].itemId == itemId && storage[i].count > 0)
                    {
                        count += storage[i].count;
                    }
                }
            }

            return (int)Math.Min(int.MaxValue, count);
        }

        // Takes from every slot holding the item, splitting proliferator points like vanilla.
        private static int TakeItem(StationComponent station, int itemId, int count, out int inc)
        {
            StationStore[] storage = station.storage;
            int taken = 0;
            inc = 0;
            lock (storage)
            {
                for (int i = 0; i < storage.Length && taken < count; i++)
                {
                    if (storage[i].itemId != itemId || storage[i].count <= 0)
                    {
                        continue;
                    }

                    int amount = Math.Min(count - taken, storage[i].count);
                    int slotInc = storage[i].inc / storage[i].count * amount;
                    storage[i].count -= amount;
                    storage[i].inc -= slotInc;
                    taken += amount;
                    inc += slotInc;
                }
            }

            return taken;
        }

        // The Subspace Warper slot first, then any item slot holding Subspace Warpers.
        private static bool TryConsumeWarper(StationComponent station)
        {
            if (StationSettings.TryConsumeSubspaceWarper(station))
            {
                return true;
            }

            int inc;
            return TakeItem(station, SubspaceWarperContent.ItemId, 1, out inc) == 1;
        }

        private static long GetFreeSpace(Player player, DeliveryPackage.GRID grid, int itemId)
        {
            long space = Math.Max(0, grid.stackSizeModified - grid.count);
            StorageComponent inventory = player.package;
            int stackSize = StorageComponent.itemStackCount[itemId];
            if (inventory?.grids == null)
            {
                return space;
            }

            for (int i = 0; i < inventory.size; i++)
            {
                StorageComponent.GRID slot = inventory.grids[i];
                if (slot.itemId == itemId)
                {
                    space += Math.Max(0, stackSize - slot.count);
                }
                else if (slot.itemId == 0 && (slot.filter == 0 || slot.filter == itemId))
                {
                    space += stackSize;
                }
            }

            return space;
        }

        private static long CountInFlight(int itemId)
        {
            long count = 0;
            foreach (Trip trip in Trips)
            {
                if (trip.ItemId == itemId)
                {
                    count += trip.Count;
                }
            }

            return count;
        }

        private static void AdvanceTrips(Player player)
        {
            for (int i = Trips.Count - 1; i >= 0; i--)
            {
                Trip trip = Trips[i];
                if (--trip.RemainingTicks == SubspaceDroneAnimation.ArrivalTicks)
                {
                    SubspaceDroneAnimation.Arrive(trip.ItemId, trip.Count);
                }

                if (trip.RemainingTicks > 0)
                {
                    continue;
                }

                Trips.RemoveAt(i);
                Deliver(player, trip);
            }
        }

        // The delivery package slot fills first and the inventory takes the rest. Anything that still doesn't fit
        // goes back to the restocker, or is dropped beside Icarus if the restocker is gone or no longer has a slot for it.
        private static void Deliver(Player player, Trip trip)
        {
            int gridIndex = trip.GridIndex >= 0 && trip.GridIndex < DeliveryPackage.MAX_COUNT ? trip.GridIndex : 0;
            int remainInc;
            int added = player.packageUtility.AddItemToAllPackages(
                trip.ItemId,
                trip.Count,
                gridIndex,
                trip.Inc,
                out remainInc,
                -1);
            int left = trip.Count - added;
            if (left <= 0)
            {
                return;
            }

            StationComponent station = FindRestocker(trip.RestockerKey);
            if (station != null && station.AddItem(trip.ItemId, left, remainInc) > 0)
            {
                return;
            }

            if (left > 0)
            {
                player.TryAddItemToPackage(trip.ItemId, left, remainInc, true);
            }
        }

        private static StationComponent FindRestocker(long key)
        {
            int planetId = (int)(key >> 32);
            int entityId = (int)(uint)key;
            PlanetFactory factory = GameMain.galaxy?.PlanetById(planetId)?.factory;
            if (factory?.entityPool == null || entityId <= 0 || entityId >= factory.entityPool.Length)
            {
                return null;
            }

            int stationId = factory.entityPool[entityId].stationId;
            StationComponent[] pool = factory.transport?.stationPool;
            StationComponent station = pool != null && stationId > 0 && stationId < pool.Length ? pool[stationId] : null;
            return IsRestockerEntity(factory, station) && station.entityId == entityId ? station : null;
        }

        private static long GetKey(PlanetFactory factory, StationComponent station)
        {
            return ((long)factory.planetId << 32) | (uint)station.entityId;
        }

        internal static void Export(BinaryWriter writer)
        {
            writer.Write(SaveFormatVersion);
            writer.Write(Trips.Count);
            foreach (Trip trip in Trips)
            {
                writer.Write(trip.RestockerKey);
                writer.Write(trip.ItemId);
                writer.Write(trip.Count);
                writer.Write(trip.Inc);
                writer.Write(trip.GridIndex);
                writer.Write(trip.RemainingTicks);
            }
        }

        internal static void Import(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version != SaveFormatVersion)
            {
                throw new InvalidDataException("Unsupported Subspace Restocker save format: " + version + ".");
            }

            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumSavedTrips)
            {
                throw new InvalidDataException("Invalid Subspace Drone trip count: " + count + ".");
            }

            List<Trip> loaded = new List<Trip>(count);
            for (int i = 0; i < count; i++)
            {
                Trip trip = new Trip
                {
                    RestockerKey = reader.ReadInt64(),
                    ItemId = reader.ReadInt32(),
                    Count = reader.ReadInt32(),
                    Inc = reader.ReadInt32(),
                    GridIndex = reader.ReadInt32(),
                    RemainingTicks = reader.ReadInt32()
                };
                if (trip.ItemId <= 0 || trip.Count <= 0 || trip.Inc < 0)
                {
                    throw new InvalidDataException("Invalid Subspace Drone trip data.");
                }

                loaded.Add(trip);
            }

            Reset();
            Trips.AddRange(loaded);
        }

        internal static void Reset()
        {
            Trips.Clear();
            Restockers.Clear();
            _registry = new Dictionary<StationComponent, long>();
            SubspaceDroneAnimation.Reset();
            _lastScanTick = -1;
        }
    }

    [HarmonyPatch(typeof(PlanetTransport), nameof(PlanetTransport.NewStationComponent))]
    internal static class SubspaceRestockerBuildPatch
    {
        private static void Postfix(PlanetTransport __instance, StationComponent __result)
        {
            SubspaceRestockerManager.OnStationCreated(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GameTick))]
    internal static class SubspaceRestockerTickPatch
    {
        private static void Postfix(Player __instance, long time)
        {
            SubspaceRestockerManager.GameTick(__instance, time);
        }
    }
}
