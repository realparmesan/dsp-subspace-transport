using System;
using System.Collections.Generic;
using System.IO;

namespace SubspaceTransport
{
    internal static class StationSettings
    {
        internal const int SubspaceWarperMaxCount = 100;

        private const int LegacySaveFormatVersion = 1;
        private const int SaveFormatVersion = 2;
        private const int MaximumSavedStations = 1000000;
        private const int MaximumSavedWarperCount = 1000000;

        private sealed class StationState
        {
            internal bool WormholeEnabled;
            internal int SubspaceWarperCount;
        }

        // Read from logistics worker threads and written from the UI thread.
        private static readonly object Sync = new object();
        private static readonly Dictionary<long, StationState> States = new Dictionary<long, StationState>();

        internal static bool IsWormholeStation(StationComponent station)
        {
            return station != null
                && station.isStellar
                && !station.isCollector
                && !station.isVeinCollector
                && station.planetId > 0
                && station.entityId > 0;
        }

        internal static bool IsEnabled(StationComponent station)
        {
            if (!IsWormholeStation(station))
            {
                return false;
            }

            lock (Sync)
            {
                StationState state;
                return States.TryGetValue(GetStationKey(station), out state) && state.WormholeEnabled;
            }
        }

        internal static void SetEnabled(StationComponent station, bool enabled)
        {
            if (!IsWormholeStation(station))
            {
                throw new ArgumentException("Cannot change the setting for an invalid station.", nameof(station));
            }

            lock (Sync)
            {
                long key = GetStationKey(station);
                GetOrCreateUnsafe(key).WormholeEnabled = enabled;
                PruneUnsafe(key);
            }
        }

        internal static int GetSubspaceWarperCount(StationComponent station)
        {
            if (!IsWormholeStation(station))
            {
                return 0;
            }

            lock (Sync)
            {
                StationState state;
                return States.TryGetValue(GetStationKey(station), out state) ? state.SubspaceWarperCount : 0;
            }
        }

        internal static bool HasSubspaceWarperSpace(StationComponent station)
        {
            return IsWormholeStation(station) && GetSubspaceWarperCount(station) < SubspaceWarperMaxCount;
        }

        // Callers check HasSubspaceWarperSpace first; like vanilla's warper slot, a belt stack may overshoot the limit.
        internal static void AddSubspaceWarpers(StationComponent station, int count)
        {
            if (count <= 0 || !IsWormholeStation(station))
            {
                return;
            }

            lock (Sync)
            {
                StationState state = GetOrCreateUnsafe(GetStationKey(station));
                state.SubspaceWarperCount = (int)Math.Min(int.MaxValue, (long)state.SubspaceWarperCount + count);
            }
        }

        internal static int TakeAllSubspaceWarpers(StationComponent station)
        {
            if (!IsWormholeStation(station))
            {
                return 0;
            }

            lock (Sync)
            {
                long key = GetStationKey(station);
                StationState state;
                if (!States.TryGetValue(key, out state))
                {
                    return 0;
                }

                int count = state.SubspaceWarperCount;
                state.SubspaceWarperCount = 0;
                PruneUnsafe(key);
                return count;
            }
        }

        internal static bool TryConsumeSubspaceWarper(StationComponent station)
        {
            if (!IsWormholeStation(station))
            {
                return false;
            }

            lock (Sync)
            {
                long key = GetStationKey(station);
                StationState state;
                if (!States.TryGetValue(key, out state) || state.SubspaceWarperCount <= 0)
                {
                    return false;
                }

                state.SubspaceWarperCount--;
                PruneUnsafe(key);
                return true;
            }
        }

        // Entity IDs are reused, so a removed station must not leave its settings behind for the next one.
        internal static void ForgetStation(StationComponent station)
        {
            if (station == null || station.planetId <= 0 || station.entityId <= 0)
            {
                return;
            }

            lock (Sync)
            {
                States.Remove(GetStationKey(station));
            }
        }

        internal static void Export(BinaryWriter writer)
        {
            lock (Sync)
            {
                writer.Write(SaveFormatVersion);
                writer.Write(States.Count);
                foreach (KeyValuePair<long, StationState> entry in States)
                {
                    writer.Write(entry.Key);
                    writer.Write(entry.Value.WormholeEnabled);
                    writer.Write(entry.Value.SubspaceWarperCount);
                }
            }
        }

        internal static void Import(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version != SaveFormatVersion && version != LegacySaveFormatVersion)
            {
                throw new InvalidDataException("Unsupported Subspace Transport save format: " + version + ".");
            }

            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumSavedStations)
            {
                throw new InvalidDataException("Invalid Subspace Transport station setting count: " + count + ".");
            }

            Dictionary<long, StationState> loadedStates = new Dictionary<long, StationState>();
            for (int i = 0; i < count; i++)
            {
                long key = reader.ReadInt64();
                StationState state = new StationState { WormholeEnabled = true };
                if (version >= SaveFormatVersion)
                {
                    state.WormholeEnabled = reader.ReadBoolean();
                    state.SubspaceWarperCount = reader.ReadInt32();
                    if (state.SubspaceWarperCount < 0 || state.SubspaceWarperCount > MaximumSavedWarperCount)
                    {
                        throw new InvalidDataException(
                            "Invalid Subspace Warper count for a station: " + state.SubspaceWarperCount + ".");
                    }
                }

                loadedStates[key] = state;
            }

            lock (Sync)
            {
                States.Clear();
                foreach (KeyValuePair<long, StationState> entry in loadedStates)
                {
                    States.Add(entry.Key, entry.Value);
                }
            }
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                States.Clear();
            }
        }

        private static StationState GetOrCreateUnsafe(long key)
        {
            StationState state;
            if (!States.TryGetValue(key, out state))
            {
                state = new StationState();
                States.Add(key, state);
            }

            return state;
        }

        private static void PruneUnsafe(long key)
        {
            StationState state;
            if (States.TryGetValue(key, out state) && !state.WormholeEnabled && state.SubspaceWarperCount <= 0)
            {
                States.Remove(key);
            }
        }

        private static long GetStationKey(StationComponent station)
        {
            return ((long)station.planetId << 32) | (uint)station.entityId;
        }
    }
}
