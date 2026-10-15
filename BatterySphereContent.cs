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
        internal const int TechnologyId = 1951;
        internal const int ItemId = 11997;
        internal const int RecipeId = 11997;

        // Research cost is copied from Artificial Star.
        private const int CostReferenceTechnologyId = 1144;
        private const int SmallCarrierRocketItemId = 1503;
        private const int EnergyExchangerItemId = 2209;

        private const string NameKey = "SubspaceTransport_BatteryRocket";
        private const string DescriptionKey = "SubspaceTransport_BatteryRocketDescription";
        private const string RecipeNameKey = "SubspaceTransport_BatteryRocketRecipe";

        private const string TechNameKey = "SubspaceTransport_BatterySphereEngineeringTech";
        private const string TechDescriptionKey = "SubspaceTransport_BatterySphereEngineeringTechDescription";
        private const string TechConclusionKey = "SubspaceTransport_BatterySphereEngineeringTechConclusion";

        internal static ManualLogSource Logger;

        // Must run from Awake; see SubspaceWarperContent.RegisterTranslations.
        internal static void RegisterTranslations()
        {
            LocalizationModule.RegisterTranslation(TechNameKey, "Battery Sphere Engineering");
            LocalizationModule.RegisterTranslation(
                TechDescriptionKey,
                "Construct a dedicated sphere project that charges accumulators for subspace logistics.");
            LocalizationModule.RegisterTranslation(
                TechConclusionKey,
                "Plan a Battery Sphere at a star in the Dyson Sphere editor, then launch Battery Rockets there. " +
                "Its dedicated output charges empty accumulators, which enabled interstellar logistics stations " +
                "can exchange for charged ones.");
            LocalizationModule.RegisterTranslation(NameKey, "Battery Rocket");
            LocalizationModule.RegisterTranslation(
                DescriptionKey,
                "A project rocket that expands a Battery Sphere to charge accumulators. " +
                "Plan a Battery Sphere at the star in the Dyson Sphere editor before launching.");
            LocalizationModule.RegisterTranslation(RecipeNameKey, "Battery Rocket");
        }

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
            TechProto costReference = LDB.techs.Select(CostReferenceTechnologyId);
            ItemProto carrierRocket = LDB.items.Select(SmallCarrierRocketItemId);
            if (vanillaResearch == null
                || costReference?.Items == null
                || costReference.ItemPoints == null
                || carrierRocket == null
                || LDB.items.Select(EnergyExchangerItemId) == null)
            {
                throw new InvalidOperationException(
                    "Could not find the required research, Artificial Star, rocket, or Energy Exchanger prototype.");
            }

            int gridIndex = Plugin.GetTabGridIndex(1, 1);
            ItemProto batteryRocket = ProtoRegistry.RegisterItem(
                ItemId,
                NameKey,
                DescriptionKey,
                IconLoader.BatteryRocket,
                gridIndex,
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
                    EnergyExchangerItemId
                },
                new[] { 1, 1 },
                new[] { ItemId },
                new[] { 1 },
                DescriptionKey,
                TechnologyId,
                gridIndex,
                RecipeNameKey,
                IconLoader.BatteryRocket);

            TechProto technology = new TechProto
            {
                ID = TechnologyId,
                Name = TechNameKey,
                Desc = TechDescriptionKey,
                Conclusion = TechConclusionKey,
                IconPath = IconLoader.BatterySphereEngineeringTech,
                IconTag = vanillaResearch.IconTag,
                IsHiddenTech = false,
                PreItem = Array.Empty<int>(),
                Position = vanillaResearch.Position + new Vector2(8f, 0f),
                PreTechs = new[] { SubspaceLogisticsTechnology.TechnologyId },
                PreTechsImplicit = Array.Empty<int>(),
                Items = (int[])costReference.Items.Clone(),
                ItemPoints = (int[])costReference.ItemPoints.Clone(),
                HashNeeded = costReference.GetHashNeeded(costReference.Level),
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
            SubspaceLogisticsTechnology.RequireRegisteredId(technology, TechnologyId);
            Logger.LogInfo("Registered the Battery Rocket, recipe, and technology.");
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
            get { return SubspaceLogisticsTechnology.IsTechUnlocked(TechnologyId); }
        }
    }
}
