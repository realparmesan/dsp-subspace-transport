using System;
using System.Linq;
using BepInEx.Logging;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    // Subspace Restocking unlocks the Subspace Restocker, a depot whose Subspace Drones fetch items for Icarus'
    // delivery package from anywhere in the cluster, and the Subspace Drone that it houses.
    internal static class SubspaceRestockerContent
    {
        internal const int TechnologyId = 1955;

        // StorageComponent.AddItem rejects item IDs of 12000 and up, so the items sit just below this mod's others.
        internal const int RestockerItemId = 11995;
        internal const int RestockerRecipeId = 11995;
        internal const int DroneItemId = 11994;
        internal const int DroneRecipeId = 11994;

        // Model indices are saved with each building and must stay below ModelProto.MAX_ARRAY_SIZE. More
        // Megastructures uses 352-357 and 552-562.
        internal const int RestockerModelId = 680;

        // Ten columns like the vanilla depots; the last two grids are reserved for Subspace Drones and Warpers.
        internal const int StorageColumns = 10;
        internal const int StorageRows = 8;
        internal const int DroneStackSize = 10;

        // Research cost is copied from Artificial Star, like Battery Sphere Engineering.
        private const int CostReferenceTechnologyId = 1144;
        private const int DepotMk2ItemId = 2102;
        private const int LogisticsBotItemId = 5003;
        private const int ProcessorItemId = 1303;

        private static readonly Color Tint = new Color(0.72f, 0.45f, 1f);

        private const string RestockerNameKey = "SubspaceTransport_SubspaceRestocker";
        private const string RestockerDescriptionKey = "SubspaceTransport_SubspaceRestockerDescription";
        private const string RestockerRecipeNameKey = "SubspaceTransport_SubspaceRestockerRecipe";
        private const string DroneNameKey = "SubspaceTransport_SubspaceDrone";
        private const string DroneDescriptionKey = "SubspaceTransport_SubspaceDroneDescription";
        private const string DroneRecipeNameKey = "SubspaceTransport_SubspaceDroneRecipe";
        private const string TechNameKey = "SubspaceTransport_SubspaceRestockingTech";
        private const string TechDescriptionKey = "SubspaceTransport_SubspaceRestockingTechDescription";
        private const string TechConclusionKey = "SubspaceTransport_SubspaceRestockingTechConclusion";

        internal static ManualLogSource Logger;

        // Must run from Awake; see SubspaceWarperContent.RegisterTranslations.
        internal static void RegisterTranslations()
        {
            LocalizationModule.RegisterTranslation(TechNameKey, "Subspace Restocking");
            LocalizationModule.RegisterTranslation(
                TechDescriptionKey,
                "Subspace Drones fetch items for Icarus' delivery package from Subspace Restockers anywhere in the cluster.");
            LocalizationModule.RegisterTranslation(
                TechConclusionKey,
                "Build Subspace Restockers and stock them with items, Subspace Drones and Subspace Warpers. " +
                "When a delivery package slot falls below its \"at least\" level, a drone tops it up to its " +
                "\"at most\" level. Each trip uses one Subspace Warper.");
            LocalizationModule.RegisterTranslation(RestockerNameKey, "Subspace Restocker");
            LocalizationModule.RegisterTranslation(
                RestockerDescriptionKey,
                "A depot that restocks Icarus' delivery package through subspace from anywhere in the cluster. " +
                "Its last two slots hold the Subspace Drones that make the trips and the Subspace Warpers they use. " +
                "Lock other slots to an item to reserve space for it.");
            LocalizationModule.RegisterTranslation(RestockerRecipeNameKey, "Subspace Restocker");
            LocalizationModule.RegisterTranslation(DroneNameKey, "Subspace Drone");
            LocalizationModule.RegisterTranslation(
                DroneDescriptionKey,
                "A heavy logistics drone that crosses subspace. Kept in a Subspace Restocker, it carries 100 times " +
                "a Logistics Bot's load to Icarus in one trip.");
            LocalizationModule.RegisterTranslation(DroneRecipeNameKey, "Subspace Drone");
        }

        internal static void Register()
        {
            TechProto techConflict = Array.Find(
                LDB.techs.dataArray,
                proto => proto != null && proto.ID == TechnologyId);
            ItemProto itemConflict = Array.Find(
                LDB.items.dataArray,
                item => item != null && (item.ID == RestockerItemId || item.ID == DroneItemId));
            RecipeProto recipeConflict = Array.Find(
                LDB.recipes.dataArray,
                recipe => recipe != null && (recipe.ID == RestockerRecipeId || recipe.ID == DroneRecipeId));
            ModelProto modelConflict = Array.Find(
                LDB.models.dataArray,
                model => model != null && model.ID == RestockerModelId);
            string conflict = techConflict != null ? "technology " + techConflict.ID + " (" + techConflict.name + ")"
                : itemConflict != null ? "item " + itemConflict.ID + " (" + itemConflict.name + ")"
                : recipeConflict != null ? "recipe " + recipeConflict.ID + " (" + recipeConflict.name + ")"
                : modelConflict != null ? "model " + modelConflict.ID + " (" + modelConflict.PrefabPath + ")"
                : null;
            if (conflict != null)
            {
                throw new InvalidOperationException(
                    "A Subspace Restocker prototype ID is already in use by " + conflict + ".");
            }

            TechProto vanillaResearch = LDB.techs.Select(SubspaceLogisticsTechnology.InterstellarLogisticsTechnologyId);
            TechProto costReference = LDB.techs.Select(CostReferenceTechnologyId);
            ItemProto depot = LDB.items.Select(DepotMk2ItemId);
            ModelProto depotModel = depot != null ? LDB.models.Select(depot.ModelIndex) : null;
            if (vanillaResearch == null
                || costReference?.Items == null
                || costReference.ItemPoints == null
                || depotModel == null
                || LDB.items.Select(LogisticsBotItemId) == null
                || LDB.items.Select(ProcessorItemId) == null)
            {
                throw new InvalidOperationException(
                    "Could not find the required research, Artificial Star, Depot Mk.II, Logistics Bot or Processor prototype.");
            }

            RegisterRestocker(depot, depotModel);
            RegisterDrone();

            TechProto technology = new TechProto
            {
                ID = TechnologyId,
                Name = TechNameKey,
                Desc = TechDescriptionKey,
                Conclusion = TechConclusionKey,
                IconPath = IconLoader.SubspaceRestockingTech,
                IconTag = vanillaResearch.IconTag,
                IsHiddenTech = false,
                PreItem = Array.Empty<int>(),
                Position = FindFreePosition(vanillaResearch.Position),
                PreTechs = new[] { SubspaceLogisticsTechnology.TechnologyId },
                PreTechsImplicit = Array.Empty<int>(),
                Items = (int[])costReference.Items.Clone(),
                ItemPoints = (int[])costReference.ItemPoints.Clone(),
                HashNeeded = costReference.GetHashNeeded(costReference.Level),
                UnlockRecipes = new[] { RestockerRecipeId, DroneRecipeId },
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
            ProtoRegistry.onLoadingFinished += ConfigureModel;
            Logger.LogInfo("Registered the Subspace Restocker, Subspace Drone and Subspace Restocking research.");
        }

        private static void RegisterRestocker(ItemProto depot, ModelProto depotModel)
        {
            int gridIndex = Plugin.GetTabGridIndex(3, 1);
            ItemProto restocker = ProtoRegistry.RegisterItem(
                RestockerItemId,
                RestockerNameKey,
                RestockerDescriptionKey,
                IconLoader.SubspaceRestocker,
                gridIndex,
                depot.StackSize,
                EItemType.Logistics);

            ModelProto model = ProtoRegistry.RegisterModel(
                RestockerModelId,
                restocker,
                depotModel.PrefabPath,
                null,
                depot.DescFields != null ? (int[])depot.DescFields.Clone() : Array.Empty<int>(),
                FindFreeBuildIndex(depot.BuildIndex));
            SubspaceLogisticsTechnology.RequireRegisteredId(model, RestockerModelId);

            // Health, ruins and draw order are proto data rather than prefab data, so they come from the depot.
            model.ObjectType = depotModel.ObjectType;
            model.RuinType = depotModel.RuinType;
            model.RendererType = depotModel.RendererType;
            model.RotSymmetry = depotModel.RotSymmetry;
            model.HpMax = depotModel.HpMax;
            model.HpUpgrade = depotModel.HpUpgrade;
            model.HpRecover = depotModel.HpRecover;
            model.RuinId = depotModel.RuinId;
            model.RuinCount = depotModel.RuinCount;
            model.RuinLifeTime = depotModel.RuinLifeTime;
            model.Order = depotModel.Order;

            ProtoRegistry.RegisterRecipe(
                RestockerRecipeId,
                ERecipeType.Assemble,
                600,
                new[] { DepotMk2ItemId, ProcessorItemId, SubspaceWarperContent.ItemId },
                new[] { 1, 4, 2 },
                new[] { RestockerItemId },
                new[] { 1 },
                RestockerDescriptionKey,
                TechnologyId,
                gridIndex,
                RestockerRecipeNameKey,
                IconLoader.SubspaceRestocker);
        }

        private static void RegisterDrone()
        {
            int gridIndex = Plugin.GetTabGridIndex(2, 2);
            ProtoRegistry.RegisterItem(
                DroneItemId,
                DroneNameKey,
                DroneDescriptionKey,
                IconLoader.SubspaceDrone,
                gridIndex,
                DroneStackSize,
                EItemType.Logistics,
                ProtoRegistry.GetDefaultIconDesc(Tint, new Color(0.2f, 0.85f, 1f)));

            RecipeProto recipe = ProtoRegistry.RegisterRecipe(
                DroneRecipeId,
                ERecipeType.Assemble,
                480,
                new[] { LogisticsBotItemId, SubspaceWarperContent.ItemId },
                new[] { 2, 1 },
                new[] { DroneItemId },
                new[] { 1 },
                DroneDescriptionKey,
                TechnologyId,
                gridIndex,
                DroneRecipeNameKey,
                IconLoader.SubspaceDrone);
            recipe.Handcraft = false;
            recipe.Explicit = true;
        }

        // Runs once the model's prefab has loaded: enlarges its storage and tints it violet. PrefabDesc copies the
        // prefab's material arrays, so replacing entries here leaves the vanilla depot untouched.
        private static void ConfigureModel()
        {
            ModelProto model = LDB.models.Select(RestockerModelId);
            PrefabDesc desc = model != null ? model.prefabDesc : null;
            if (desc == null || !desc.isStorage)
            {
                Logger.LogError("The Subspace Restocker model did not load as a storage building.");
                return;
            }

            desc.storageCol = StorageColumns;
            desc.storageRow = StorageRows;

            for (int lod = 0; lod < desc.lodCount; lod++)
            {
                Material[] materials = desc.lodMaterials?[lod];
                if (materials == null)
                {
                    continue;
                }

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        continue;
                    }

                    Material tinted = new Material(materials[i]);
                    if (tinted.HasProperty("_Color"))
                    {
                        tinted.SetColor("_Color", tinted.GetColor("_Color") * Tint);
                    }

                    materials[i] = tinted;
                }
            }
        }

        // The build bar shows one item per index, so the restocker takes the first free slot in the depot's row.
        private static int FindFreeBuildIndex(int depotBuildIndex)
        {
            int row = depotBuildIndex / 100 * 100;
            for (int column = 1; column <= 10; column++)
            {
                int candidate = row + column;
                if (!LDB.items.dataArray.Any(item => item != null && item.BuildIndex == candidate))
                {
                    return candidate;
                }
            }

            Logger.LogWarning("No free build bar slot next to the depots; the Subspace Restocker is not on the build bar.");
            return 0;
        }

        // Battery Sphere Engineering sits 8 columns right of Interstellar Logistics System; this tech goes in a
        // free cell beside it, so both branch off Subspace Logistics in parallel.
        private static Vector2 FindFreePosition(Vector2 interstellarLogistics)
        {
            Vector2[] occupied = LDB.techs.dataArray
                .Where(proto => proto != null && proto.page == 0)
                .Select(proto => proto.Position)
                .Concat(new[]
                {
                    // This mod's other main-tab techs are still pending in LDBTool.
                    interstellarLogistics + new Vector2(4f, 0f),
                    interstellarLogistics + new Vector2(8f, 0f),
                    HyperMassContent.InducerPosition
                })
                .ToArray();
            Vector2[] offsets =
            {
                new Vector2(8f, 4f),
                new Vector2(8f, -8f),
                new Vector2(8f, 8f),
                new Vector2(12f, 4f)
            };

            foreach (Vector2 offset in offsets)
            {
                Vector2 candidate = interstellarLogistics + offset;
                if (!occupied.Any(position => (position - candidate).sqrMagnitude < 4f))
                {
                    return candidate;
                }
            }

            Logger.LogWarning("No free technology tree cell found for Subspace Restocking; nodes may overlap.");
            return interstellarLogistics + offsets[0];
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
