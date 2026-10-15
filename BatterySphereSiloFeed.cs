using HarmonyLib;

namespace SubspaceTransport
{
    // Vertical launching silos take both Small Carrier Rockets and Battery Rockets through belts, sorters and
    // the silo window. An empty, idle silo follows whichever rocket type is offered; the launch then goes to the
    // Dyson Sphere or the Battery Sphere according to the rocket and the star's planned sphere type.
    internal static class BatterySphereSiloFeed
    {
        private static bool IsRocket(int itemId)
        {
            return itemId == ItemProto.kRocketId || itemId == BatterySphereContent.ItemId;
        }

        // The other rocket type, offered to the silo's feeders only while it is empty and idle.
        internal static int AlternateRocket(int bulletId)
        {
            return bulletId == BatterySphereContent.ItemId ? ItemProto.kRocketId : BatterySphereContent.ItemId;
        }

        internal static void Retarget(PlanetFactory factory, int siloId, int itemId)
        {
            if (!IsRocket(itemId)
                || !BatterySphereContent.IsUnlocked
                || factory == null
                || factory.factorySystem == null
                || siloId <= 0
                || siloId >= factory.factorySystem.siloPool.Length)
            {
                return;
            }

            ref SiloComponent silo = ref factory.factorySystem.siloPool[siloId];
            if (silo.id != siloId || silo.bulletId == itemId || silo.bulletCount > 0 || silo.time > 0)
            {
                return;
            }

            lock (factory.entityMutexs[silo.entityId])
            {
                if (silo.bulletCount > 0 || silo.bulletId == itemId)
                {
                    return;
                }

                silo.bulletId = itemId;
                if (silo.needs != null)
                {
                    silo.needs[0] = itemId;
                }
            }
        }
    }

    [HarmonyPatch(typeof(SiloComponent), "InternalUpdate")]
    internal static class BatterySphereSiloNeedsPatch
    {
        private static void Postfix(SiloComponent __instance)
        {
            if (__instance.needs == null || __instance.needs.Length < 2)
            {
                return;
            }

            bool open = BatterySphereContent.IsUnlocked && __instance.bulletCount == 0 && __instance.time == 0;
            __instance.needs[1] = open ? BatterySphereSiloFeed.AlternateRocket(__instance.bulletId) : 0;
        }
    }

    [HarmonyPatch(
        typeof(PlanetFactory),
        "InsertInto",
        new[] { typeof(int), typeof(int), typeof(int), typeof(byte), typeof(byte), typeof(byte) },
        new[]
        {
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Out
        })]
    internal static class BatterySphereSiloInsertEntityPatch
    {
        private static void Prefix(PlanetFactory __instance, int entityId, int itemId)
        {
            int siloId = __instance.entityPool[entityId].siloId;
            if (siloId > 0)
            {
                BatterySphereSiloFeed.Retarget(__instance, siloId, itemId);
            }
        }
    }

    [HarmonyPatch(
        typeof(PlanetFactory),
        "InsertInto",
        new[] { typeof(uint), typeof(int), typeof(int), typeof(byte), typeof(byte), typeof(byte) },
        new[]
        {
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Out
        })]
    internal static class BatterySphereSiloInsertTargetPatch
    {
        private static void Prefix(PlanetFactory __instance, uint ioTargetTypedId, int itemId)
        {
            if ((EFactoryIOTargetType)(ioTargetTypedId & 0xFF000000u) == EFactoryIOTargetType.Silo)
            {
                BatterySphereSiloFeed.Retarget(__instance, (int)(ioTargetTypedId & 0xFFFFFF), itemId);
            }
        }
    }
}
