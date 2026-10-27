using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    // Stellar Hyper Mass Inducer unlocks the Hyper Mass Rocket and the Hyper Mass Injector project. Stellar Hyper
    // Mass Amplification adds levels that raise the luminosity a hyperinjected star reaches.
    internal static class HyperMassContent
    {
        internal const int TechnologyId = 1953;

        // An Upgrades tab tech (IDs of 2000 and up). It used to be 1954 on the main tab, so its names changed too:
        // LDBTool pins each name to an ID in its config.
        internal const int AmplificationTechnologyId = 2970;
        internal const int ItemId = 11998;
        internal const int RecipeId = 11998;
        internal const int AmplificationMaximumLevel = 50;

        // Luminosity (as the star detail panel shows it) after hyperinjection: 2.5 L with the inducer alone,
        // plus 0.02 L for each of 50 Amplification levels, reaching 3.5 L.
        internal const float BaseLuminosity = 2.5f;
        internal const float LuminosityPerLevel = 0.02f;

        private const int SmallCarrierRocketItemId = 1503;
        private const int SubspaceWarperCount = 2;

        // Vein Utilization is found by its unlock function rather than by ID; its last (repeatable) tech sets the
        // research cost. Falls back to Dyson Sphere Stress System if it can't be found.
        private const int MiningCostUnlockFunction = 20;

        private const string NameKey = "SubspaceTransport_HyperMassRocket";
        private const string DescriptionKey = "SubspaceTransport_HyperMassRocketDescription";
        private const string RecipeNameKey = "SubspaceTransport_HyperMassRocketRecipe";

        private const string TechNameKey = "SubspaceTransport_StellarHyperMassInducerTech";
        private const string TechDescriptionKey = "SubspaceTransport_StellarHyperMassInducerTechDescription";
        private const string TechConclusionKey = "SubspaceTransport_StellarHyperMassInducerTechConclusion";

        private const string AmplificationNameKey = "SubspaceTransport_StellarHyperMassAmplificationUpgrade";
        private const string AmplificationDescriptionKey =
            "SubspaceTransport_StellarHyperMassAmplificationUpgradeDescription";
        private const string AmplificationConclusionKey =
            "SubspaceTransport_StellarHyperMassAmplificationUpgradeConclusion";

        internal static ManualLogSource Logger;

        // The inducer's tree cell, for later techs that look for free cells before LDBTool adds it to LDB.techs.
        internal static Vector2 InducerPosition { get; private set; }

        // Must run from Awake; see SubspaceWarperContent.RegisterTranslations.
        internal static void RegisterTranslations()
        {
            LocalizationModule.RegisterTranslation(TechNameKey, "Stellar Hyper Mass Inducer");
            LocalizationModule.RegisterTranslation(
                TechDescriptionKey,
                "Pull matter from other galaxies through subspace and feed it into a star, " +
                "turning it into a hypergiant.");
            LocalizationModule.RegisterTranslation(
                TechConclusionKey,
                "Plan a Hyper Mass Injector at a star in the Dyson Sphere editor and launch Hyper Mass Rockets there. " +
                "Once its sphere generates " + HyperMassManager.ActivationPower + ", " +
                "Hyperinject Mass into the Star to raise it to " + BaseLuminosity.ToString("0.0#") + " L.");
            LocalizationModule.RegisterTranslation(AmplificationNameKey, "Stellar Hyper Mass Amplification");
            LocalizationModule.RegisterTranslation(
                AmplificationDescriptionKey,
                "Widen the subspace aperture to inject more mass into hyperinjected stars.");
            LocalizationModule.RegisterTranslation(
                AmplificationConclusionKey,
                "Each level raises the luminosity of a hyperinjected star by " +
                LuminosityPerLevel.ToString("0.0#") + " L. Hyperinject the star again to apply it.");
            LocalizationModule.RegisterTranslation(NameKey, "Hyper Mass Rocket");
            LocalizationModule.RegisterTranslation(
                DescriptionKey,
                "A project rocket that builds a Hyper Mass Injector sphere. " +
                "Plan a Hyper Mass Injector at the star in the Dyson Sphere editor before launching.");
            LocalizationModule.RegisterTranslation(RecipeNameKey, "Hyper Mass Rocket");
        }

        internal static void Register()
        {
            string conflict = null;
            TechProto techConflict = Array.Find(
                LDB.techs.dataArray,
                proto => proto != null
                    && (proto.ID == TechnologyId || proto.ID == AmplificationTechnologyId));
            ItemProto itemConflict = Array.Find(LDB.items.dataArray, item => item != null && item.ID == ItemId);
            RecipeProto recipeConflict = Array.Find(
                LDB.recipes.dataArray,
                recipe => recipe != null && recipe.ID == RecipeId);
            if (techConflict != null)
            {
                conflict = "technology " + techConflict.ID + " (" + techConflict.name + ")";
            }
            else if (itemConflict != null)
            {
                conflict = "item " + itemConflict.ID + " (" + itemConflict.name + ")";
            }
            else if (recipeConflict != null)
            {
                conflict = "recipe " + recipeConflict.ID + " (" + recipeConflict.name + ")";
            }

            if (conflict != null)
            {
                throw new InvalidOperationException(
                    "A Stellar Hyper Mass prototype ID is already in use by " + conflict + ".");
            }

            TechProto vanillaResearch = LDB.techs.Select(SubspaceLogisticsTechnology.InterstellarLogisticsTechnologyId);
            ItemProto carrierRocket = LDB.items.Select(SmallCarrierRocketItemId);
            if (vanillaResearch == null || carrierRocket == null)
            {
                throw new InvalidOperationException(
                    "Could not find the Interstellar Logistics System research or the Small Carrier Rocket.");
            }

            TechProto costReference = GetCostReference();

            int gridIndex = Plugin.GetTabGridIndex(1, 2);
            ItemProto rocket = ProtoRegistry.RegisterItem(
                ItemId,
                NameKey,
                DescriptionKey,
                IconLoader.HyperMassRocket,
                gridIndex,
                20,
                carrierRocket.Type,
                ProtoRegistry.GetDefaultIconDesc(new Color(1f, 0.25f, 0.3f), new Color(0.6f, 0.3f, 1f)));
            rocket.ModelIndex = carrierRocket.ModelIndex;
            rocket.ModelCount = carrierRocket.ModelCount;
            rocket.BuildMode = carrierRocket.BuildMode;
            rocket.BuildIndex = carrierRocket.BuildIndex;
            rocket.SubID = carrierRocket.SubID;
            rocket.IsEntity = carrierRocket.IsEntity;
            rocket.CanBuild = carrierRocket.CanBuild;
            rocket.prefabDesc = carrierRocket.prefabDesc;

            ProtoRegistry.RegisterRecipe(
                RecipeId,
                ERecipeType.Assemble,
                1800,
                new[] { SmallCarrierRocketItemId, SubspaceWarperContent.ItemId },
                new[] { 1, SubspaceWarperCount },
                new[] { ItemId },
                new[] { 1 },
                DescriptionKey,
                TechnologyId,
                gridIndex,
                RecipeNameKey,
                IconLoader.HyperMassRocket);

            Vector2 inducerPosition = FindFreePosition(
                vanillaResearch.Position,
                new Vector2(8f, -4f),
                new Vector2(8f, 8f),
                new Vector2(8f, -8f),
                new Vector2(12f, -4f));
            InducerPosition = inducerPosition;
            TechProto inducer = CreateTechnology(
                TechnologyId,
                TechNameKey,
                TechDescriptionKey,
                TechConclusionKey,
                vanillaResearch.IconTag,
                inducerPosition,
                SubspaceLogisticsTechnology.TechnologyId,
                costReference);
            inducer.HashNeeded = costReference.GetHashNeeded(costReference.Level);
            inducer.UnlockRecipes = new[] { RecipeId };
            LDBTool.PreAddProto(inducer);
            SubspaceLogisticsTechnology.RequireRegisteredId(inducer, TechnologyId);

            Vector2 amplificationPosition = UpgradesTab.GetPosition(1);
            TechProto amplification = CreateTechnology(
                AmplificationTechnologyId,
                AmplificationNameKey,
                AmplificationDescriptionKey,
                AmplificationConclusionKey,
                vanillaResearch.IconTag,
                amplificationPosition,
                TechnologyId,
                costReference);

            // Level 1 costs the same as the inducer; later levels follow the reference tech's cost curve.
            amplification.HashNeeded = costReference.GetHashNeeded(costReference.Level)
                - costReference.LevelCoef1 - costReference.LevelCoef2;
            amplification.Level = 1;
            amplification.MaxLevel = AmplificationMaximumLevel;
            amplification.LevelCoef1 = costReference.LevelCoef1;
            amplification.LevelCoef2 = costReference.LevelCoef2;
            LDBTool.PreAddProto(amplification);
            SubspaceLogisticsTechnology.RequireRegisteredId(amplification, AmplificationTechnologyId);

            Logger.LogInfo(
                "Registered the Hyper Mass Rocket, recipe, and Stellar Hyper Mass technologies " +
                "(cost copied from tech " + costReference.ID + ").");
        }

        private static TechProto CreateTechnology(
            int id,
            string nameKey,
            string descriptionKey,
            string conclusionKey,
            string iconTag,
            Vector2 position,
            int prerequisite,
            TechProto costReference)
        {
            return new TechProto
            {
                ID = id,
                Name = nameKey,
                Desc = descriptionKey,
                Conclusion = conclusionKey,
                IconPath = IconLoader.StellarHyperMassInducerTech,
                IconTag = iconTag,
                IsHiddenTech = false,
                PreItem = Array.Empty<int>(),
                Position = position,
                // Upgrades-tab techs list their prerequisite as implicit: it is still required, but the tab has no
                // node for it, so a drawn link would run off the page.
                PreTechs = id >= 2000 ? Array.Empty<int>() : new[] { prerequisite },
                PreTechsImplicit = id >= 2000 ? new[] { prerequisite } : Array.Empty<int>(),
                Items = (int[])costReference.Items.Clone(),
                ItemPoints = (int[])costReference.ItemPoints.Clone(),
                UnlockRecipes = Array.Empty<int>(),
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
        }

        private static TechProto GetCostReference()
        {
            TechProto veinUtilization = LDB.techs.dataArray
                .Where(proto => proto?.UnlockFunctions != null
                    && proto.Items != null
                    && proto.ItemPoints != null
                    && proto.UnlockFunctions.Contains(MiningCostUnlockFunction))
                .OrderBy(proto => proto.ID)
                .LastOrDefault();
            if (veinUtilization != null)
            {
                return veinUtilization;
            }

            Logger.LogWarning("Could not find Vein Utilization; Stellar Hyper Mass research uses Stress System's cost.");
            return SubspaceLogisticsTechnology.GetCostReference();
        }

        // The first cell, relative to origin, that no vanilla tech or earlier mod tech already occupies.
        private static Vector2 FindFreePosition(Vector2 origin, params Vector2[] offsets)
        {
            Vector2 interstellarLogistics = LDB.techs
                .Select(SubspaceLogisticsTechnology.InterstellarLogisticsTechnologyId)
                .Position;
            List<Vector2> occupied = LDB.techs.dataArray
                .Where(proto => proto != null && proto.page == 0)
                .Select(proto => proto.Position)
                .ToList();

            // This mod's other main-tab techs are still pending in LDBTool and not in LDB.techs yet: Subspace
            // Logistics, Advanced Energy Engineering and Battery Sphere Engineering.
            occupied.Add(interstellarLogistics + new Vector2(4f, 0f));
            occupied.Add(interstellarLogistics + new Vector2(8f, 0f));
            occupied.Add(BatterySphereContent.TechnologyPosition);
            occupied.Add(origin);

            foreach (Vector2 offset in offsets)
            {
                Vector2 candidate = origin + offset;
                if (!occupied.Any(position => (position - candidate).sqrMagnitude < 4f))
                {
                    return candidate;
                }
            }

            Logger.LogWarning("No free technology tree cell found near " + origin + "; nodes may overlap.");
            return origin + offsets[0];
        }

        internal static void LinkTechnologyTree()
        {
            GameHistoryData history = GameMain.data != null && GameMain.data.history != null
                ? GameMain.data.history
                : GameMain.history;
            SubspaceLogisticsTechnology.EnsureTechState(history, TechnologyId);
            SubspaceLogisticsTechnology.EnsureTechState(history, AmplificationTechnologyId);
        }

        internal static bool IsUnlocked
        {
            get { return SubspaceLogisticsTechnology.IsTechUnlocked(TechnologyId); }
        }

        // Levels start at 1, so curLevel is the level being researched until the last level sets unlocked.
        internal static int AmplificationLevels
        {
            get
            {
                GameHistoryData history = GameMain.data != null ? GameMain.data.history : GameMain.history;
                if (history?.techStates == null || !history.techStates.ContainsKey(AmplificationTechnologyId))
                {
                    return 0;
                }

                TechState state = history.TechState(AmplificationTechnologyId);
                if (state.unlocked)
                {
                    return AmplificationMaximumLevel;
                }

                return Mathf.Clamp(state.curLevel - 1, 0, AmplificationMaximumLevel);
            }
        }

        // The luminosity a hyperinjection reaches with the current research, or 0 before the inducer is researched.
        internal static float TargetLuminosity
        {
            get { return IsUnlocked ? BaseLuminosity + LuminosityPerLevel * AmplificationLevels : 0f; }
        }

        internal static float MaximumLuminosity
        {
            get { return BaseLuminosity + LuminosityPerLevel * AmplificationMaximumLevel; }
        }
    }
}
