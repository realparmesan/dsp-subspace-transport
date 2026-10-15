using System;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    internal static class SubspaceWarperContent
    {
        // IDs are unchanged from the former Hyperdrive Module so existing saves keep their items.
        internal const int ItemId = 11996;
        internal const int RecipeId = 11996;
        private const int SpaceWarperItemId = 1210;
        private const int GravitonLensItemId = 1209;

        private const string NameKey = "SubspaceTransport_SubspaceWarper";
        private const string DescriptionKey = "SubspaceTransport_SubspaceWarperDescription";
        private const string RecipeNameKey = "SubspaceTransport_SubspaceWarperRecipe";

        // CommonAPI only adds translation keys while the game loads its localization settings,
        // which happens before LDBTool's proto callbacks, so this must run from Awake.
        internal static void RegisterTranslations()
        {
            LocalizationModule.RegisterTranslation(NameKey, "Subspace Warper");
            LocalizationModule.RegisterTranslation(
                DescriptionKey,
                "A stabilized spacetime-curvature warper consumed by subspace logistics. " +
                "Interstellar Logistics Stations keep them in a dedicated slot.");
            LocalizationModule.RegisterTranslation(RecipeNameKey, "Subspace Warper");
        }

        internal static void Register()
        {
            ItemProto existingItem = Array.Find(
                LDB.items.dataArray,
                item => item != null && item.ID == ItemId);
            if (existingItem != null)
            {
                throw new InvalidOperationException(
                    "Subspace Warper item ID " + ItemId + " is already in use by " +
                    "'" + existingItem.name + "'.");
            }

            if (Array.Exists(
                LDB.recipes.dataArray,
                existing => existing != null && existing.ID == RecipeId))
            {
                throw new InvalidOperationException(
                    "Subspace Warper recipe ID " + RecipeId + " is already in use.");
            }

            if (LDB.items.Select(SpaceWarperItemId) == null || LDB.items.Select(GravitonLensItemId) == null)
            {
                throw new InvalidOperationException(
                    "Could not find the vanilla Space Warper or Graviton Lens item.");
            }

            int gridIndex = Plugin.GetTabGridIndex(2, 1);
            ProtoRegistry.RegisterItem(
                ItemId,
                NameKey,
                DescriptionKey,
                IconLoader.SubspaceWarper,
                gridIndex,
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
                DescriptionKey,
                SubspaceLogisticsTechnology.TechnologyId,
                gridIndex,
                RecipeNameKey,
                IconLoader.SubspaceWarper);
            // Assembler-only: keeps it out of the Icarus replicator.
            recipe.Handcraft = false;
            recipe.Explicit = true;

            Plugin.ModLogger.LogInfo("Registered the Subspace Warper item and recipe.");
        }
    }
}
