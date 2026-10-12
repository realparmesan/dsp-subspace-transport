using System;
using BepInEx.Logging;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    internal static class BatterySphereContent
    {
        internal const int TechnologyId = 11999;
        internal const int ItemId = 11997;
        internal const int RecipeId = 11997;

        private const int SmallCarrierRocketItemId = 1503;
        private const int EnergyExchangerItemId = 2208;

        internal static ManualLogSource Logger;

        internal static void Register()
        {
            if (Array.Exists(
                    LDB.techs.dataArray,
                    proto => proto != null && proto.ID == TechnologyId)
                || Array.Exists(
                    LDB.items.dataArray,
                    item => item != null && item.ID == ItemId)
                || Array.Exists(
                    LDB.recipes.dataArray,
                    recipe => recipe != null && recipe.ID == RecipeId))
            {
                throw new InvalidOperationException("A Battery Sphere prototype ID is already in use.");
            }

            TechProto vanillaResearch = LDB.techs.Select(SubspaceLogisticsTechnology.InterstellarLogisticsTechnologyId);
            ItemProto carrierRocket = LDB.items.Select(SmallCarrierRocketItemId);
            if (vanillaResearch == null
                || carrierRocket == null
                || LDB.items.Select(EnergyExchangerItemId) == null
                || LDB.items.Select(HyperdriveModuleContent.ItemId) == null)
            {
                throw new InvalidOperationException(
                    "Could not find the required research, rocket, Energy Exchanger, or Hyperdrive Module prototype.");
            }

            LocalizationModule.RegisterTranslation(
                "SubspaceTransport_BatterySphereRocket",
                "Battery Sphere Rocket");
            LocalizationModule.RegisterTranslation(
                "SubspaceTransport_BatterySphereRocketDescription",
                "A project rocket that expands a dedicated virtual Dyson Sphere to charge accumulators.");
            LocalizationModule.RegisterTranslation(
                "SubspaceTransport_BatterySphereRocketRecipe",
                "Battery Sphere Rocket");

            ItemProto batteryRocket = ProtoRegistry.RegisterItem(
                ItemId,
                "SubspaceTransport_BatterySphereRocket",
                "SubspaceTransport_BatterySphereRocketDescription",
                carrierRocket.IconPath,
                2810,
                20,
                carrierRocket.Type,
                ProtoRegistry.GetDefaultIconDesc(Color.cyan, Color.magenta));
            batteryRocket.ModelIndex = carrierRocket.ModelIndex;
            batteryRocket.ModelCount = carrierRocket.ModelCount;
            batteryRocket.BuildMode = carrierRocket.BuildMode;
            batteryRocket.BuildIndex = carrierRocket.BuildIndex;
            batteryRocket.SubID = carrierRocket.SubID;
            batteryRocket.IsEntity = carrierRocket.IsEntity;
            batteryRocket.CanBuild = carrierRocket.CanBuild;
            batteryRocket.prefabDesc = carrierRocket.prefabDesc;

            ProtoRegistry.RegisterRecipe(
                RecipeId,
                ERecipeType.Assemble,
                1800,
                new[]
                {
                    SmallCarrierRocketItemId,
                    EnergyExchangerItemId,
                    HyperdriveModuleContent.ItemId
                },
                new[] { 1, 4, 2 },
                new[] { ItemId },
                new[] { 1 },
                "SubspaceTransport_BatterySphereRocketDescription",
                TechnologyId,
                2810,
                "SubspaceTransport_BatterySphereRocketRecipe",
                carrierRocket.IconPath);

            TechProto technology = new TechProto
            {
                ID = TechnologyId,
                Name = "Battery Sphere Engineering",
                Desc = "Construct a dedicated sphere project that charges accumulators for wormhole logistics.",
                Conclusion =
                    "Battery Sphere Rockets build a separate sphere project. Its dedicated output charges empty " +
                    "accumulators, which enabled interstellar logistics stations can exchange for charged ones.",
                IconPath = vanillaResearch.IconPath,
                IconTag = vanillaResearch.IconTag,
                IsHiddenTech = false,
                PreItem = Array.Empty<int>(),
                Position = vanillaResearch.Position + new Vector2(6f, 1f),
                PreTechs = new[] { SubspaceLogisticsTechnology.TechnologyId },
                PreTechsImplicit = Array.Empty<int>(),
                Items = (int[])vanillaResearch.Items.Clone(),
                ItemPoints = (int[])vanillaResearch.ItemPoints.Clone(),
                HashNeeded = vanillaResearch.HashNeeded,
                UnlockRecipes = new[] { RecipeId },
                UnlockFunctions = Array.Empty<int>(),
                UnlockValues = Array.Empty<double>(),
                Published = true,
                Level = 0,
                MaxLevel = 0,
                LevelCoef1 = 0,
                LevelCoef2 = 0,
                IsLabTech = true,
                PreTechsMax = false,
                AddItems = Array.Empty<int>(),
                AddItemCounts = Array.Empty<int>(),
                PropertyOverrideItems = Array.Empty<int>(),
                PropertyItemCounts = Array.Empty<int>()
            };

            LDBTool.PreAddProto(technology);
            Logger.LogInfo("Registered the Battery Sphere rocket, recipe, and technology.");
        }

        internal static void LinkTechnologyTree()
        {
            SubspaceLogisticsTechnology.EnsureTechState(
                GameMain.data != null && GameMain.data.history != null
                    ? GameMain.data.history
                    : GameMain.history,
                TechnologyId);
        }

        internal static bool IsUnlocked
        {
            get
            {
                GameHistoryData history = GameMain.data != null ? GameMain.data.history : GameMain.history;
                if (history?.techStates == null || !history.techStates.ContainsKey(TechnologyId))
                {
                    return false;
                }

                return history.TechState(TechnologyId).unlocked;
            }
        }
    }
}
