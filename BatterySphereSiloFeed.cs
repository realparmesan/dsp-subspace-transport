using HarmonyLib;

namespace SubspaceTransport
{
    // Vertical launching silos take Small Carrier Rockets, Battery Rockets and Hyper Mass Rockets through belts,
    // sorters and the silo window. An empty, idle silo follows whichever rocket type is offered; the launch then
    // goes to the Dyson Sphere, Battery Sphere or Hyper Mass Injector according to the rocket and the star's
    // planned sphere type.
    internal static class BatterySphereSiloFeed
    {
        private static readonly int[] Rockets =
        {
            ItemProto.kRocketId,
            BatterySphereContent.ItemId,
            HyperMassContent.ItemId
        };

        private static bool IsRocket(int itemId)
        {
            return itemId == ItemProto.kRocketId
                || (itemId == BatterySphereContent.ItemId && BatterySphereContent.IsUnlocked)
                || (itemId == HyperMassContent.ItemId && HyperMassContent.IsUnlocked);
        }

        // Fills needs[1] and needs[2] with the unlocked rocket types the silo isn't loaded with, offered to its
        // feeders only while it is empty and idle.
        internal static void OfferAlternateRockets(int[] needs, int bulletId, bool open)
        {
            int slot = 1;
            foreach (int rocket in Rockets)
            {
                if (open && rocket != bulletId && IsRocket(rocket) && slot < 3)
                {
                    needs[slot++] = rocket;
                }
            }

            while (slot < 3)
            {
                needs[slot++] = 0;
            }
        }

        internal static void Retarget(PlanetFactory factory, int siloId, int itemId)
        {
            if (!IsRocket(itemId)
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
            if (__instance.needs == null || __instance.needs.Length < 3)
            {
                return;
            }

            bool open = __instance.bulletCount == 0 && __instance.time == 0;
            BatterySphereSiloFeed.OfferAlternateRockets(__instance.needs, __instance.bulletId, open);
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
