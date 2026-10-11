using System;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    internal static class HyperdriveModuleContent
    {
        internal const int ItemId = 20010;
        internal const int RecipeId = 20010;
        private const int SpaceWarperItemId = 1210;
        private const int GravitonLensItemId = 1209;

        internal static void Register()
        {
            if (LDB.items.Select(ItemId) != null || LDB.recipes.Select(RecipeId) != null)
            {
                throw new InvalidOperationException(
                    "Hyperdrive Module item or recipe ID is already in use.");
            }

            ItemProto spaceWarper = LDB.items.Select(SpaceWarperItemId);
            if (spaceWarper == null || LDB.items.Select(GravitonLensItemId) == null)
            {
                throw new InvalidOperationException(
                    "Could not find the vanilla Space Warper or Graviton Lens item.");
            }

            LocalizationModule.RegisterTranslation(
                "SubspaceTransport_HyperdriveModule",
                "Hyperdrive Module");
            LocalizationModule.RegisterTranslation(
                "SubspaceTransport_HyperdriveModuleDescription",
                "A stabilized spacetime-curvature module used to power wormhole logistics.");

            ProtoRegistry.RegisterItem(
                ItemId,
                "SubspaceTransport_HyperdriveModule",
                "SubspaceTransport_HyperdriveModuleDescription",
                spaceWarper.IconPath,
                2801,
                100,
                EItemType.Component,
                ProtoRegistry.GetDefaultIconDesc(Color.cyan, Color.blue));

            ProtoRegistry.RegisterRecipe(
                RecipeId,
                ERecipeType.Assemble,
                360,
                new[] { SpaceWarperItemId, GravitonLensItemId },
                new[] { 4, 2 },
                new[] { ItemId },
                new[] { 1 },
                "SubspaceTransport_HyperdriveModuleDescription",
                SubspaceLogisticsTechnology.TechnologyId,
                2801,
                spaceWarper.IconPath);

            Plugin.ModLogger.LogInfo("Registered the Hyperdrive Module item and recipe.");
        }
    }
}
