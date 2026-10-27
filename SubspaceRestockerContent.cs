using System;
using System.Linq;
using BepInEx.Logging;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    // Subspace Restocking unlocks the Subspace Restocker, a small planetary logistics station whose Subspace Drones
    // fetch items for Icarus' delivery package from anywhere in the cluster, and the Subspace Drone that it houses.
    internal static class SubspaceRestockerContent
    {
        internal const int TechnologyId = 1955;

        // StorageComponent.AddItem rejects item IDs of 12000 and up, so the items sit just below this mod's others.
        internal const int RestockerItemId = 11995;
        internal const int RestockerRecipeId = 11995;
        internal const int DroneItemId = 11994;
        internal const int DroneRecipeId = 11994;

        // Model indices are saved with each building and must stay below ModelProto.MAX_ARRAY_SIZE. More
        // Megastructures uses 352-357 and 552-562. Restockers built before 0.7.0 were depots on model 680, which
        // stays registered so those saves still load; they now behave as plain depots.
        internal const int RestockerModelId = 681;
        private const int LegacyDepotModelId = 680;

        // Five item slots like an Interstellar Logistics Station, the most the Control Panel's station list shows;
        // Subspace Drones and Warpers have their own slots.
        internal const int StorageSlots = 5;
        internal const int DroneStackSize = 10;
        internal const int DroneSlotCapacity = 10;

        // The legacy depot's grid, so restockers built before 0.7.0 keep their contents.
        private const int LegacyStorageColumns = 10;
        private const int LegacyStorageRows = 8;

        // The model is a Planetary Logistics Station shrunk to this size. Belt ports and sorter slots move inward by
        // whole grid cells (1000 per circumference on a radius-200 planet) so belts still line up with the grid.
        private const float ModelScale = 0.7f;
        private const float GridCellLength = 1.2566371f;

        // Research cost is copied from Artificial Star, like Battery Sphere Engineering.
        private const int CostReferenceTechnologyId = 1144;
        private const int DepotMk2ItemId = 2102;
        private const int PlanetaryStationItemId = 2103;
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
                "Build Subspace Restockers, set their item slots and stock them with Subspace Drones and " +
                "Subspace Warpers. " +
                "When a delivery package slot falls below its \"at least\" level, a drone tops it up to its " +
                "\"at most\" level. Each trip uses one Subspace Warper.");
            LocalizationModule.RegisterTranslation(RestockerNameKey, "Subspace Restocker");
            LocalizationModule.RegisterTranslation(
                RestockerDescriptionKey,
                "A small logistics station that restocks Icarus' delivery package through subspace from anywhere " +
                "in the cluster. It has five item slots, a slot for the Subspace Drones that make the trips and a " +
                "slot for the Subspace Warpers they use, and trades with other stations on its planet like a " +
                "Planetary Logistics Station.");
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
                model => model != null && (model.ID == RestockerModelId || model.ID == LegacyDepotModelId));
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
            ItemProto station = LDB.items.Select(PlanetaryStationItemId);
            ModelProto stationModel = station != null ? LDB.models.Select(station.ModelIndex) : null;
            if (vanillaResearch == null
                || costReference?.Items == null
                || costReference.ItemPoints == null
                || depotModel == null
                || stationModel == null
                || LDB.items.Select(LogisticsBotItemId) == null
                || LDB.items.Select(ProcessorItemId) == null)
            {
                throw new InvalidOperationException(
                    "Could not find the required research, Artificial Star, Depot Mk.II, Planetary Logistics Station, " +
                    "Logistics Bot or Processor prototype.");
            }

            RegisterRestocker(station, stationModel);
            RegisterLegacyDepotModel(depotModel);
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

        private static void RegisterRestocker(ItemProto station, ModelProto stationModel)
        {
            int gridIndex = Plugin.GetTabGridIndex(3, 1);
            ItemProto restocker = ProtoRegistry.RegisterItem(
                RestockerItemId,
                RestockerNameKey,
                RestockerDescriptionKey,
                IconLoader.SubspaceRestocker,
                gridIndex,
                station.StackSize,
                EItemType.Logistics);

            ModelProto model = ProtoRegistry.RegisterModel(
                RestockerModelId,
                restocker,
                stationModel.PrefabPath,
                null,
                station.DescFields != null ? (int[])station.DescFields.Clone() : Array.Empty<int>(),
                FindFreeBuildIndex(station.BuildIndex));
            SubspaceLogisticsTechnology.RequireRegisteredId(model, RestockerModelId);
            CopyModelData(stationModel, model);

            ProtoRegistry.RegisterRecipe(
                RestockerRecipeId,
                ERecipeType.Assemble,
                600,
                new[] { PlanetaryStationItemId, ProcessorItemId, SubspaceWarperContent.ItemId },
                new[] { 1, 4, 2 },
                new[] { RestockerItemId },
                new[] { 1 },
                RestockerDescriptionKey,
                TechnologyId,
                gridIndex,
                RestockerRecipeNameKey,
                IconLoader.SubspaceRestocker);
        }

        // Not linked to the item, so it is never offered on the build bar; it only renders restockers saved as depots.
        private static void RegisterLegacyDepotModel(ModelProto depotModel)
        {
            ModelProto model = ProtoRegistry.RegisterModel(LegacyDepotModelId, depotModel.PrefabPath);
            SubspaceLogisticsTechnology.RequireRegisteredId(model, LegacyDepotModelId);
            CopyModelData(depotModel, model);
        }

        // Health, ruins and draw order are proto data rather than prefab data, so they come from the vanilla model.
        private static void CopyModelData(ModelProto source, ModelProto model)
        {
            model.ObjectType = source.ObjectType;
            model.RuinType = source.RuinType;
            model.RendererType = source.RendererType;
            model.RotSymmetry = source.RotSymmetry;
            model.HpMax = source.HpMax;
            model.HpUpgrade = source.HpUpgrade;
            model.HpRecover = source.HpRecover;
            model.RuinId = source.RuinId;
            model.RuinCount = source.RuinCount;
            model.RuinLifeTime = source.RuinLifeTime;
            model.Order = source.Order;
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

        // Runs once the models' prefabs have loaded. PrefabDesc copies the prefab's meshes, poses and material
        // arrays, so changing them here leaves the vanilla station and depot untouched.
        private static void ConfigureModel()
        {
            ModelProto model = LDB.models.Select(RestockerModelId);
            PrefabDesc desc = model != null ? model.prefabDesc : null;
            if (desc == null || !desc.isStation || desc.isStellarStation)
            {
                Logger.LogError("The Subspace Restocker model did not load as a planetary logistics station.");
            }
            else
            {
                desc.stationMaxItemKinds = StorageSlots;
                ScaleModel(model, desc, ModelScale);
                ApplyTint(desc);
            }

            ModelProto legacy = LDB.models.Select(LegacyDepotModelId);
            PrefabDesc legacyDesc = legacy != null ? legacy.prefabDesc : null;
            if (legacyDesc == null || !legacyDesc.isStorage)
            {
                Logger.LogError("The legacy Subspace Restocker depot model did not load as a storage building.");
                return;
            }

            legacyDesc.storageCol = LegacyStorageColumns;
            legacyDesc.storageRow = LegacyStorageRows;
            ApplyTint(legacyDesc);
        }

        private static void ApplyTint(PrefabDesc desc)
        {
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

        // Shrinks the meshes, their vertex animation, colliders and the points drones, belts and sorters use.
        private static void ScaleModel(ModelProto model, PrefabDesc desc, float scale)
        {
            for (int lod = 0; lod < desc.lodCount; lod++)
            {
                Mesh mesh = desc.lodMeshes?[lod];
                if (mesh != null)
                {
                    Vector3[] vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] *= scale;
                    }

                    Bounds bounds = mesh.bounds;
                    mesh.vertices = vertices;
                    mesh.bounds = new Bounds(bounds.center * scale, bounds.size * scale);
                }

                // Animated parts are drawn from per-frame vertex positions, the first three floats of each vertex.
                VertaBuffer verta = desc.lodVertas?[lod];
                if (verta?.data != null && verta.vertexSize >= 3)
                {
                    for (int i = 0; i + 2 < verta.data.Length; i += verta.vertexSize)
                    {
                        verta.data[i] *= scale;
                        verta.data[i + 1] *= scale;
                        verta.data[i + 2] *= scale;
                    }

                    verta.SetBufferData();
                }
            }

            if (desc.lodMeshes != null && desc.lodMeshes.Length > 0 && desc.lodMeshes[0] != null)
            {
                model.meshBounds = desc.lodMeshes[0].bounds;
            }

            ScaleColliders(desc.colliders, scale);
            ScaleColliders(desc.buildColliders, scale);
            desc.buildCollider = ScaleCollider(desc.buildCollider, scale);

            if (desc.landPoints != null)
            {
                for (int i = 0; i < desc.landPoints.Length; i++)
                {
                    desc.landPoints[i] = new Vector3(
                        desc.landPoints[i].x * scale,
                        desc.landPoints[i].y,
                        desc.landPoints[i].z * scale);
                }
            }

            MovePosesInward(desc.portPoses, scale);
            MovePosesInward(desc.slotPoses, scale);

            desc.stationDronePos *= scale;
            desc.stationShipPos *= scale;
            desc.powerPoint *= scale;
            desc.selectCenter *= scale;
            desc.selectSize *= scale;
            desc.buildingGizmoCenter *= scale;
            desc.buildingGizmoExt *= scale;
            desc.minimapScl *= scale;
            desc.roughRadius *= scale;
            desc.roughHeight *= scale;
            desc.barHeight *= scale;
            desc.signHeight *= scale;
            desc.cullingHeight *= scale;
        }

        private static void ScaleColliders(ColliderData[] colliders, float scale)
        {
            if (colliders == null)
            {
                return;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i] = ScaleCollider(colliders[i], scale);
            }
        }

        private static ColliderData ScaleCollider(ColliderData collider, float scale)
        {
            collider.pos *= scale;
            collider.ext *= scale;
            collider.radius *= scale;
            return collider;
        }

        // Each pose moves toward the centre by the whole number of grid cells nearest to the scaled distance, keeping
        // its height and its offset from the grid.
        private static void MovePosesInward(Pose[] poses, float scale)
        {
            if (poses == null)
            {
                return;
            }

            for (int i = 0; i < poses.Length; i++)
            {
                Vector3 position = poses[i].position;
                poses[i].position = new Vector3(
                    MoveInward(position.x, scale),
                    position.y,
                    MoveInward(position.z, scale));
            }
        }

        private static float MoveInward(float coordinate, float scale)
        {
            float cells = Mathf.Round(Mathf.Abs(coordinate) * (1f - scale) / GridCellLength);
            float distance = Mathf.Max(0f, Mathf.Abs(coordinate) - cells * GridCellLength);
            return Mathf.Sign(coordinate) * distance;
        }

        // The build bar shows one item per index, so the restocker takes the first free slot in the stations' row.
        private static int FindFreeBuildIndex(int stationBuildIndex)
        {
            int row = stationBuildIndex / 100 * 100;
            for (int column = 1; column <= 10; column++)
            {
                int candidate = row + column;
                if (!LDB.items.dataArray.Any(item => item != null && item.BuildIndex == candidate))
                {
                    return candidate;
                }
            }

            Logger.LogWarning("No free build bar slot next to the logistics stations; the Subspace Restocker is not on the build bar.");
            return 0;
        }

        // Advanced Energy Engineering sits 8 columns right of Interstellar Logistics System; this tech goes in a
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
                    BatterySphereContent.TechnologyPosition,
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
