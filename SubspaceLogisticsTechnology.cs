using System;
using System.Linq;
using BepInEx.Logging;
using CommonAPI.Systems.ModLocalization;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    internal static class SubspaceLogisticsTechnology
    {
        // Vanilla uses 1901-1904 and More Megastructures uses 1918-1931, so this mod's techs sit at 1950+.
        internal const int TechnologyId = 1950;
        internal const int InterstellarLogisticsTechnologyId = 1605;

        // Research cost is copied from the first level of Dyson Sphere Stress System.
        internal const int CostReferenceTechnologyId = 1523;

        // LDBTool pins each proto's ID in LDBTool.CustomID.cfg under its Name, so these keys must not be
        // reused with a different ID. (0.5.x used the English names at IDs 1919/1920.)
        private const string NameKey = "SubspaceTransport_WormholeLogisticsTech";
        private const string DescriptionKey = "SubspaceTransport_WormholeLogisticsTechDescription";
        private const string ConclusionKey = "SubspaceTransport_WormholeLogisticsTechConclusion";

        internal static ManualLogSource Logger;

        // Must run from Awake; see SubspaceWarperContent.RegisterTranslations.
        internal static void RegisterTranslations()
        {
            LocalizationModule.RegisterTranslation(NameKey, "Wormhole Logistics");
            LocalizationModule.RegisterTranslation(
                DescriptionKey,
                "Instantly transfer cargo between interstellar logistics stations.");
            LocalizationModule.RegisterTranslation(
                ConclusionKey,
                "Interstellar logistics stations can transfer cargo through wormholes without logistics vessels. " +
                "Enable Wormhole Transport in each station's settings. Each transfer consumes one Subspace Warper " +
                "from the sending station, or from the receiving station if the sender has none, " +
                "and still requires station power. " +
                "Research Wormhole Efficiency to reduce the energy cost.");
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

            TechProto prerequisite = LDB.techs.Select(InterstellarLogisticsTechnologyId);
            if (prerequisite == null)
            {
                throw new InvalidOperationException(
                    "Could not find the Interstellar Logistics System technology (1605).");
            }
            if (prerequisite.Items == null || prerequisite.ItemPoints == null)
            {
                throw new InvalidOperationException(
                    "The Interstellar Logistics System technology has no research cost data.");
            }

            TechProto costReference = GetCostReference();

            TechProto technology = new TechProto
            {
                ID = TechnologyId,
                Name = NameKey,
                Desc = DescriptionKey,
                Conclusion = ConclusionKey,
                IconPath = IconLoader.WormholeLogisticsTech,
                IconTag = prerequisite.IconTag,
                IsHiddenTech = false,
                PreItem = Array.Empty<int>(),
                Position = prerequisite.Position + new Vector2(4f, 0f),
                PreTechs = new[] { InterstellarLogisticsTechnologyId },
                PreTechsImplicit = Array.Empty<int>(),
                Items = (int[])costReference.Items.Clone(),
                ItemPoints = (int[])costReference.ItemPoints.Clone(),
                HashNeeded = costReference.GetHashNeeded(costReference.Level),
                UnlockRecipes = new[] { SubspaceWarperContent.RecipeId },
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
            RequireRegisteredId(technology, TechnologyId);
            Logger.LogInfo("Registered the Wormhole Logistics research technology.");
        }

        internal static TechProto GetCostReference()
        {
            TechProto costReference = LDB.techs.Select(CostReferenceTechnologyId);
            if (costReference?.Items == null || costReference.ItemPoints == null)
            {
                throw new InvalidOperationException(
                    "Could not find the research cost of the Dyson Sphere Stress System technology (" +
                    CostReferenceTechnologyId + ").");
            }

            return costReference;
        }

        internal static void LinkTechnologyTree()
        {
            LDB.techs.OnAfterDeserialize();

            foreach (TechProto technology in LDB.techs.dataArray)
            {
                technology?.Preload();
            }

            foreach (TechProto technology in LDB.techs.dataArray)
            {
                if (technology == null)
                {
                    continue;
                }

                technology.PreTechsImplicit = (technology.PreTechsImplicit ?? Array.Empty<int>())
                    .Except(technology.PreTechs ?? Array.Empty<int>())
                    .ToArray();
                technology.UnlockRecipes = (technology.UnlockRecipes ?? Array.Empty<int>())
                    .Distinct()
                    .ToArray();
                technology.Preload2();
            }

            EnsureTechState(
                GameMain.data != null && GameMain.data.history != null
                    ? GameMain.data.history
                    : GameMain.history);
        }

        // LDBTool.PreAddProto replaces the ID with any value saved in LDBTool.CustomID.cfg.
        internal static void RequireRegisteredId(Proto proto, int expectedId)
        {
            if (proto.ID != expectedId)
            {
                throw new InvalidOperationException(
                    "LDBTool assigned ID " + proto.ID + " to '" + proto.Name + "', but this mod requires ID " +
                    expectedId + ". Remove the '" + proto.Name + "' entry from BepInEx/config/LDBTool/" +
                    "LDBTool.CustomID.cfg and restart the game.");
            }
        }

        internal static void EnsureTechState(GameHistoryData history, int technologyId = TechnologyId)
        {
            if (history?.techStates == null || history.techStates.ContainsKey(technologyId))
            {
                return;
            }

            TechProto technology = LDB.techs.Select(technologyId);
            if (technology == null)
            {
                throw new InvalidOperationException(
                    "Technology " + technologyId + " was not present after LDBTool added technology prototypes.");
            }

            history.techStates.Add(
                technologyId,
                new TechState(
                    false,
                    technology.Level,
                    technology.MaxLevel,
                    0L,
                    technology.GetHashNeeded(technology.Level),
                    0));
        }

        internal static bool IsUnlocked
        {
            get { return IsTechUnlocked(TechnologyId); }
        }

        internal static bool IsTechUnlocked(int technologyId)
        {
            GameHistoryData history = GameMain.data != null ? GameMain.data.history : GameMain.history;
            if (history?.techStates == null || !history.techStates.ContainsKey(technologyId))
            {
                return false;
            }

            return history.TechState(technologyId).unlocked;
        }

        // 0.6.0 development builds saved Wormhole Logistics with four levels, which never set it unlocked
        // because the levels started at 0. Saved tech states keep their max level, so convert them here.
        internal static void MigrateTechState(GameHistoryData history)
        {
            TechProto technology = LDB.techs.Select(TechnologyId);
            if (history?.techStates == null
                || technology == null
                || !history.techStates.ContainsKey(TechnologyId))
            {
                return;
            }

            TechState state = history.techStates[TechnologyId];
            if (state.maxLevel == technology.MaxLevel)
            {
                return;
            }

            bool researched = state.unlocked || state.curLevel > technology.Level;
            state.maxLevel = technology.MaxLevel;
            state.curLevel = technology.Level;
            state.hashNeeded = technology.GetHashNeeded(technology.Level);
            state.hashUploaded = Math.Min(state.hashUploaded, state.hashNeeded);
            state.unlocked = false;
            history.techStates[TechnologyId] = state;
            if (researched)
            {
                history.UnlockTech(TechnologyId);
            }

            Logger.LogInfo("Converted the saved Wormhole Logistics research to a single level.");
        }
    }

    [HarmonyPatch(typeof(GameHistoryData), "Import")]
    internal static class WormholeLogisticsTechStateMigrationPatch
    {
        private static void Postfix(GameHistoryData __instance)
        {
            SubspaceLogisticsTechnology.MigrateTechState(__instance);
        }
    }
}
