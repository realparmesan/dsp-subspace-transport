using System;
using HarmonyLib;

namespace SubspaceTransport
{
    // Vanilla sizes a storage grid UI to whole rows, but reads storage.grids at the hovered cell without checking it
    // against storage.size. When the last row is only partly filled by slots (a size that is not a multiple of the
    // column count), hovering its missing cells throws every frame. The hover state is cleared instead.
    [HarmonyPatch(typeof(UIStorageGrid), "_OnUpdate")]
    internal static class StorageGridHoverFixPatch
    {
        private static Exception Finalizer(UIStorageGrid __instance, Exception __exception)
        {
            if (!(__exception is IndexOutOfRangeException))
            {
                return __exception;
            }

            Traverse grid = Traverse.Create(__instance);
            StorageComponent storage = grid.Field("storage").GetValue<StorageComponent>();
            int hovered = grid.Field("mouseOnX").GetValue<int>() + grid.Field("mouseOnY").GetValue<int>() * __instance.colCount;
            if (storage == null || hovered < storage.size)
            {
                return __exception;
            }

            grid.Field("mouseOnX").SetValue(-1);
            grid.Field("mouseOnY").SetValue(-1);
            return null;
        }
    }
}
