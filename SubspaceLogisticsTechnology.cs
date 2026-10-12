using System;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    internal static class SubspaceLogisticsTechnology
    {
        internal const int TechnologyId = 1919;
        internal const int InterstellarLogisticsTechnologyId = 1605;
        internal const int MaximumLevel = 4;

        internal static ManualLogSource Logger;

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

            TechProto technology = new TechProto
            {
                ID = TechnologyId,
                Name = "Wormhole Logistics",
                Desc = "Instantly transfer cargo between interstellar logistics stations.",
                Conclusion =
                    "Interstellar logistics stations can transfer cargo through wormholes without logistics vessels. " +
                    "Transfers still require station power. " +
                    "Each research level halves the energy cost, down to the normal trip cost.",
                IconPath = prerequisite.IconPath,
                IconTag = prerequisite.IconTag,
                IsHiddenTech = false,
                PreItem = Array.Empty<int>(),
                Position = prerequisite.Position + new Vector2(4f, 0f),
                PreTechs = new[] { InterstellarLogisticsTechnologyId },
                PreTechsImplicit = Array.Empty<int>(),
                Items = (int[])prerequisite.Items.Clone(),
                ItemPoints = (int[])prerequisite.ItemPoints.Clone(),
                HashNeeded = prerequisite.HashNeeded,
                UnlockRecipes = new[] { HyperdriveModuleContent.RecipeId },
                UnlockFunctions = Array.Empty<int>(),
                UnlockValues = Array.Empty<double>(),
                Published = true,
                Level = 0,
                MaxLevel = MaximumLevel,
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
            Logger.LogInfo("Registered the Wormhole Logistics research technology.");
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

        internal static int GetEnergyMultiplier(int technologyLevel)
        {
            if (technologyLevel <= 0)
            {
                return 8;
            }

            if (technologyLevel >= MaximumLevel)
            {
                return 1;
            }

            return 8 >> (technologyLevel - 1);
        }

        internal static int CurrentLevel
        {
            get
            {
                if (GameMain.data == null
                    || GameMain.data.history == null
                    || GameMain.data.history.techStates == null
                    || !GameMain.data.history.techStates.ContainsKey(TechnologyId))
                {
                    return 0;
                }

                TechState state = GameMain.data.history.TechState(TechnologyId);
                return state.unlocked ? state.curLevel : 0;
            }
        }
    }
}
