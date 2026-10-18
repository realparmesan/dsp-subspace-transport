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
    internal static class SubspaceRestockerManager
    {
        internal const int DroneSlot = 0;
        internal const int WarperSlot = 1;
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
            internal StorageComponent Storage;
            internal long Key;
        }

        private static readonly List<Trip> Trips = new List<Trip>();
        private static readonly List<Restocker> Restockers = new List<Restocker>();
        private static long _lastScanTick = -1;

        internal static void GameTick(Player player, long time)
        {
            if (player == null || player != GameMain.mainPlayer)
            {
                return;
            }

            AdvanceTrips(player);
            SubspaceDroneAnimation.GameTick();
            if (time % DispatchIntervalTicks != 0 || !SubspaceRestockerContent.IsUnlocked)
            {
                return;
            }

            if (_lastScanTick < 0 || time - _lastScanTick >= RescanIntervalTicks || time < _lastScanTick)
            {
                Rescan();
                _lastScanTick = time;
            }

            Dispatch(player);
        }

        internal static bool IsRestocker(PlanetFactory factory, StorageComponent storage)
        {
            return factory?.entityPool != null
                && storage != null
                && storage.id > 0
                && storage.entityId > 0
                && storage.entityId < factory.entityPool.Length
                && factory.entityPool[storage.entityId].protoId == SubspaceRestockerContent.RestockerItemId;
        }

        // The first two slots are locked to Subspace Drones and Subspace Warpers. They come first so that the
        // depot's automation limit, which bans slots from the end, never covers them.
        internal static void EnsureReservedSlots(StorageComponent storage)
        {
            if (storage?.grids == null || storage.size <= WarperSlot)
            {
                return;
            }

            if (storage.type <= EStorageType.Default)
            {
                storage.type = EStorageType.Filtered;
            }

            Reserve(storage, DroneSlot, SubspaceRestockerContent.DroneItemId);
            Reserve(storage, WarperSlot, SubspaceWarperContent.ItemId);
        }

        private static void Reserve(StorageComponent storage, int slot, int itemId)
        {
            StorageComponent.GRID grid = storage.grids[slot];
            if (grid.filter == itemId)
            {
                return;
            }

            // A slot already holding something else is reserved once it empties.
            if (grid.count <= 0 || grid.itemId == itemId)
            {
                storage.SetFilter(slot, itemId);
                storage.NotifyStorageChange();
            }
        }

        // A new restocker is reserved and picked up at once rather than at the next scan.
        internal static void OnStorageCreated(FactoryStorage factoryStorage, StorageComponent storage)
        {
            if (IsRestocker(factoryStorage?.factory, storage))
            {
                EnsureReservedSlots(storage);
                _lastScanTick = -1;
            }
        }

        internal static int CountBusyDrones(PlanetFactory factory, StorageComponent storage)
        {
            if (factory == null || storage == null)
            {
                return 0;
            }

            long key = GetKey(factory, storage);
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

        private static void Rescan()
        {
            Restockers.Clear();
            GameData data = GameMain.data;
            if (data?.factories == null)
            {
                return;
            }

            for (int f = 0; f < data.factoryCount && f < data.factories.Length; f++)
            {
                PlanetFactory factory = data.factories[f];
                StorageComponent[] pool = factory?.factoryStorage?.storagePool;
                if (pool == null || factory.planet == null)
                {
                    continue;
                }

                int cursor = Math.Min(factory.factoryStorage.storageCursor, pool.Length);
                for (int i = 1; i < cursor; i++)
                {
                    StorageComponent storage = pool[i];
                    if (storage == null || storage.id != i || !IsRestocker(factory, storage))
                    {
                        continue;
                    }

                    EnsureReservedSlots(storage);
                    Restockers.Add(new Restocker
                    {
                        Factory = factory,
                        Storage = storage,
                        Key = GetKey(factory, storage)
                    });
                }
            }
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
                    int warperInc;
                    if (source.Storage.TakeItem(SubspaceWarperContent.ItemId, 1, out warperInc) != 1)
                    {
                        break;
                    }

                    int inc;
                    int taken = source.Storage.TakeItem(itemId, amount, out inc);
                    if (taken <= 0)
                    {
                        source.Storage.AddItem(SubspaceWarperContent.ItemId, 1, warperInc, out _);
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
                    SubspaceDroneAnimation.Depart(source.Factory, source.Storage.entityId, itemId, taken);
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
                StorageComponent storage = restocker.Storage;
                if (!IsRestocker(restocker.Factory, storage)
                    || restocker.Factory.factoryStorage.storagePool[storage.id] != storage)
                {
                    Restockers.RemoveAt(i);
                    continue;
                }

                int stock = storage.GetItemCount(itemId);
                if (stock <= bestStock
                    || storage.GetItemCount(SubspaceWarperContent.ItemId) <= 0
                    || storage.GetItemCount(SubspaceRestockerContent.DroneItemId)
                        <= CountBusyDrones(restocker.Factory, storage))
                {
                    continue;
                }

                best = restocker;
                bestStock = stock;
            }

            return bestStock > 0;
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
        // goes back to the restocker, or is dropped beside Icarus if the restocker is gone or full.
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

            StorageComponent storage = FindRestocker(trip.RestockerKey);
            if (storage != null)
            {
                int returned = storage.AddItem(trip.ItemId, left, remainInc, out remainInc);
                left -= returned;
            }

            if (left > 0)
            {
                player.TryAddItemToPackage(trip.ItemId, left, remainInc, true);
            }
        }

        private static StorageComponent FindRestocker(long key)
        {
            int planetId = (int)(key >> 32);
            int entityId = (int)(uint)key;
            PlanetFactory factory = GameMain.galaxy?.PlanetById(planetId)?.factory;
            if (factory?.entityPool == null || entityId <= 0 || entityId >= factory.entityPool.Length)
            {
                return null;
            }

            int storageId = factory.entityPool[entityId].storageId;
            StorageComponent[] pool = factory.factoryStorage?.storagePool;
            StorageComponent storage = pool != null && storageId > 0 && storageId < pool.Length ? pool[storageId] : null;
            return IsRestocker(factory, storage) && storage.entityId == entityId ? storage : null;
        }

        private static long GetKey(PlanetFactory factory, StorageComponent storage)
        {
            return ((long)factory.planetId << 32) | (uint)storage.entityId;
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
            SubspaceDroneAnimation.Reset();
            _lastScanTick = -1;
        }
    }

    [HarmonyPatch(typeof(FactoryStorage), nameof(FactoryStorage.NewStorageComponent))]
    internal static class SubspaceRestockerBuildPatch
    {
        private static void Postfix(FactoryStorage __instance, StorageComponent __result)
        {
            SubspaceRestockerManager.OnStorageCreated(__instance, __result);
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
