using System;
using BepInEx.Logging;
using CommonAPI.Systems.ModLocalization;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    // Three research levels that reduce the station energy used by wormhole stations, from 8x the vanilla
    // trip cost before any level down to 1x at level 3.
    internal static class WormholeEfficiencyTechnology
    {
        internal const int TechnologyId = 1952;
        internal const int MaximumLevel = 3;

        private const int BaseEnergyMultiplier = 8;

        private const string NameKey = "SubspaceTransport_WormholeEfficiencyTech";
        private const string DescriptionKey = "SubspaceTransport_WormholeEfficiencyTechDescription";
        private const string ConclusionKey = "SubspaceTransport_WormholeEfficiencyTechConclusion";

        internal static ManualLogSource Logger;

        // Must run from Awake; see SubspaceWarperContent.RegisterTranslations.
        internal static void RegisterTranslations()
        {
            LocalizationModule.RegisterTranslation(NameKey, "Wormhole Efficiency");
            LocalizationModule.RegisterTranslation(
                DescriptionKey,
                "Stabilize wormhole apertures to reduce the energy that stations spend on each transfer.");
            LocalizationModule.RegisterTranslation(
                ConclusionKey,
                "Each level halves the station energy used by Wormhole Transport. " +
                "At level 3 it matches the normal logistics vessel trip cost.");
        }

        internal static void Register()
        {
            if (Array.Exists(
                LDB.techs.dataArray,
                proto => proto != null && proto.ID == TechnologyId))
            {
                throw new InvalidOperationException(
                    "Technology ID " + TechnologyId + " is already in use.");
            }

            TechProto vanillaResearch = LDB.techs.Select(SubspaceLogisticsTechnology.InterstellarLogisticsTechnologyId);
            if (vanillaResearch == null)
            {
                throw new InvalidOperationException(
                    "Could not find the Interstellar Logistics System technology (1605).");
            }

            // Same per-level cost curve as Dyson Sphere Stress System; level 1 costs the same as Wormhole Logistics.
            TechProto costReference = SubspaceLogisticsTechnology.GetCostReference();

            TechProto technology = new TechProto
            {
                ID = TechnologyId,
                Name = NameKey,
                Desc = DescriptionKey,
                Conclusion = ConclusionKey,
                IconPath = IconLoader.WormholeEfficiencyTech,
                IconTag = vanillaResearch.IconTag,
                IsHiddenTech = false,
                PreItem = Array.Empty<int>(),
                Position = vanillaResearch.Position + new Vector2(8f, 4f),
                PreTechs = new[] { SubspaceLogisticsTechnology.TechnologyId },
                PreTechsImplicit = Array.Empty<int>(),
                Items = (int[])costReference.Items.Clone(),
                ItemPoints = (int[])costReference.ItemPoints.Clone(),
                HashNeeded = costReference.HashNeeded,
                UnlockRecipes = Array.Empty<int>(),
                UnlockFunctions = Array.Empty<int>(),
                UnlockValues = Array.Empty<double>(),
                Published = true,
                Level = 1,
                MaxLevel = MaximumLevel,
                LevelCoef1 = costReference.LevelCoef1,
                LevelCoef2 = costReference.LevelCoef2,
                IsLabTech = true,
                PreTechsMax = false,
                AddItems = Array.Empty<int>(),
                AddItemCounts = Array.Empty<int>(),
                PropertyOverrideItems = Array.Empty<int>(),
                PropertyItemCounts = Array.Empty<int>()
            };

            LDBTool.PreAddProto(technology);
            SubspaceLogisticsTechnology.RequireRegisteredId(technology, TechnologyId);
            Logger.LogInfo("Registered the Wormhole Efficiency research technology.");
        }

        internal static void LinkTechnologyTree()
        {
            SubspaceLogisticsTechnology.EnsureTechState(
                GameMain.data != null && GameMain.data.history != null
                    ? GameMain.data.history
                    : GameMain.history,
                TechnologyId);
        }

        // Levels start at 1, so curLevel is the level being researched until the last level sets unlocked.
        internal static int CompletedLevels
        {
            get
            {
                GameHistoryData history = GameMain.data != null ? GameMain.data.history : GameMain.history;
                if (history?.techStates == null || !history.techStates.ContainsKey(TechnologyId))
                {
                    return 0;
                }

                TechState state = history.TechState(TechnologyId);
                if (state.unlocked)
                {
                    return MaximumLevel;
                }

                return Mathf.Clamp(state.curLevel - 1, 0, MaximumLevel);
            }
        }

        internal static int EnergyMultiplier
        {
            get { return BaseEnergyMultiplier >> CompletedLevels; }
        }
    }
}
