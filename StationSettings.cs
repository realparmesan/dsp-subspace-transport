using System;
using System.Collections.Generic;
using System.IO;

namespace SubspaceTransport
{
    internal static class StationSettings
    {
        private const int SaveFormatVersion = 1;
        private const int MaximumSavedStations = 1000000;
        private static readonly HashSet<long> EnabledStations = new HashSet<long>();

        internal static bool IsEnabled(StationComponent station)
        {
            return station != null
                && station.planetId > 0
                && station.entityId > 0
                && EnabledStations.Contains(GetStationKey(station));
        }

        internal static bool Toggle(StationComponent station)
        {
            if (station == null || station.planetId <= 0 || station.entityId <= 0)
            {
                throw new ArgumentException("Cannot change the setting for an invalid station.", nameof(station));
            }

            long key = GetStationKey(station);
            if (EnabledStations.Remove(key))
            {
                return false;
            }

            EnabledStations.Add(key);
            return true;
        }

        internal static void Export(BinaryWriter writer)
        {
            writer.Write(SaveFormatVersion);
            writer.Write(EnabledStations.Count);
            foreach (long stationKey in EnabledStations)
            {
                writer.Write(stationKey);
            }
        }

        internal static void Import(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version != SaveFormatVersion)
            {
                throw new InvalidDataException("Unsupported Subspace Transport save format: " + version + ".");
            }

            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumSavedStations)
            {
                throw new InvalidDataException("Invalid Subspace Transport station setting count: " + count + ".");
            }

            HashSet<long> loadedStations = new HashSet<long>();
            for (int i = 0; i < count; i++)
            {
                loadedStations.Add(reader.ReadInt64());
            }

            EnabledStations.Clear();
            EnabledStations.UnionWith(loadedStations);
        }

        internal static void Reset()
        {
            EnabledStations.Clear();
        }

        private static long GetStationKey(StationComponent station)
        {
            return ((long)station.planetId << 32) | (uint)station.entityId;
        }
    }
}
