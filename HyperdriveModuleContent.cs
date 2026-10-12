using System;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    internal static class HyperdriveModuleContent
    {
        internal const int ItemId = 11996;
        internal const int RecipeId = 11996;
        private const int SpaceWarperItemId = 1210;
        private const int GravitonLensItemId = 1209;

        internal static void Register()
        {
            ItemProto existingItem = Array.Find(
                LDB.items.dataArray,
                item => item != null && item.ID == ItemId);
            if (existingItem != null)
            {
                throw new InvalidOperationException(
                    "Hyperdrive Module item ID " + ItemId + " is already in use by " +
                    "'" + existingItem.name + "'.");
            }

            if (Array.Exists(
                LDB.recipes.dataArray,
                recipe => recipe != null && recipe.ID == RecipeId))
            {
                throw new InvalidOperationException(
                    "Hyperdrive Module recipe ID " + RecipeId + " is already in use.");
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
            LocalizationModule.RegisterTranslation(
                "SubspaceTransport_HyperdriveModuleRecipe",
                "Hyperdrive Module");

            ProtoRegistry.RegisterItem(
                ItemId,
                "SubspaceTransport_HyperdriveModule",
                "SubspaceTransport_HyperdriveModuleDescription",
                spaceWarper.IconPath,
                2801,
                100,
                EItemType.Component,
                ProtoRegistry.GetDefaultIconDesc(new Color(0.85f, 0.25f, 1f), new Color(0.2f, 0.85f, 1f)));

            RecipeProto recipe = ProtoRegistry.RegisterRecipe(
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
                "SubspaceTransport_HyperdriveModuleRecipe",
                spaceWarper.IconPath);
            recipe.Handcraft = true;
            recipe.Explicit = true;

            Plugin.ModLogger.LogInfo("Registered the Hyperdrive Module item and recipe.");
        }
    }
}
