using System;
using System.Linq;
using BepInEx.Logging;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace SubspaceTransport
{
    // Advanced Energy Engineering follows Subspace Logistics, leads to Battery Sphere Engineering and unlocks the
    // Advanced Energy Exchanger, an Energy Exchanger that charges and discharges accumulators at five times the power.
    internal static class AdvancedEnergyExchangerContent
    {
        internal const int TechnologyId = 1956;
        internal const int ItemId = 11993;
        internal const int RecipeId = 11993;

        // More Megastructures uses models 352-357 and 552-562; the Subspace Restocker is 681.
        internal const int ModelId = 682;

        internal const int PowerMultiplier = 5;

        // The arms move by vertex animation. Each arm hangs in a vertical plane on a diagonal; moving vertices past
        // the hinge are pushed out along that diagonal (span) and away from it (breadth), fading in between the
        // hinge radii and fading out towards the gaps between arms.
        private const float ArmSpan = 1.25f;
        private const float ArmBreadth = 1.3f;
        private const float HingeStartRadius = 1.2f;
        private const float HingeEndRadius = 2.5f;
        private const float ArmHalfAngle = 25f;

        // Research cost is copied from Artificial Star, like Battery Sphere Engineering.
        private const int CostReferenceTechnologyId = 1144;
        private const int EnergyExchangerItemId = 2209;
        private const int ProcessorItemId = 1303;

        private static readonly Color Tint = new Color(0.72f, 0.45f, 1f);

        private const string NameKey = "SubspaceTransport_AdvancedEnergyExchanger";
        private const string DescriptionKey = "SubspaceTransport_AdvancedEnergyExchangerDescription";
        private const string RecipeNameKey = "SubspaceTransport_AdvancedEnergyExchangerRecipe";
        private const string TechNameKey = "SubspaceTransport_AdvancedEnergyEngineeringTech";
        private const string TechDescriptionKey = "SubspaceTransport_AdvancedEnergyEngineeringTechDescription";
        private const string TechConclusionKey = "SubspaceTransport_AdvancedEnergyEngineeringTechConclusion";

        internal static ManualLogSource Logger;

        // Must run from Awake; see SubspaceWarperContent.RegisterTranslations.
        internal static void RegisterTranslations()
        {
            LocalizationModule.RegisterTranslation(TechNameKey, "Advanced Energy Engineering");
            LocalizationModule.RegisterTranslation(
                TechDescriptionKey,
                "Energy Exchangers that charge and discharge accumulators at five times the power.");
            LocalizationModule.RegisterTranslation(
                TechConclusionKey,
                "Build Advanced Energy Exchangers. They work like Energy Exchangers, with five times the " +
                "charging and discharging power.");
            LocalizationModule.RegisterTranslation(NameKey, "Advanced Energy Exchanger");
            LocalizationModule.RegisterTranslation(
                DescriptionKey,
                "An Energy Exchanger rebuilt with subspace capacitors. It charges and discharges accumulators at " +
                "five times the power of an Energy Exchanger.");
            LocalizationModule.RegisterTranslation(RecipeNameKey, "Advanced Energy Exchanger");
        }

        internal static void Register()
        {
            TechProto techConflict = Array.Find(
                LDB.techs.dataArray,
                proto => proto != null && proto.ID == TechnologyId);
            ItemProto itemConflict = Array.Find(
                LDB.items.dataArray,
                item => item != null && item.ID == ItemId);
            RecipeProto recipeConflict = Array.Find(
                LDB.recipes.dataArray,
                recipe => recipe != null && recipe.ID == RecipeId);
            ModelProto modelConflict = Array.Find(
                LDB.models.dataArray,
                existing => existing != null && existing.ID == ModelId);
            string conflict = techConflict != null ? "technology " + techConflict.ID + " (" + techConflict.name + ")"
                : itemConflict != null ? "item " + itemConflict.ID + " (" + itemConflict.name + ")"
                : recipeConflict != null ? "recipe " + recipeConflict.ID + " (" + recipeConflict.name + ")"
                : modelConflict != null ? "model " + modelConflict.ID + " (" + modelConflict.PrefabPath + ")"
                : null;
            if (conflict != null)
            {
                throw new InvalidOperationException(
                    "An Advanced Energy Exchanger prototype ID is already in use by " + conflict + ".");
            }

            TechProto vanillaResearch = LDB.techs.Select(SubspaceLogisticsTechnology.InterstellarLogisticsTechnologyId);
            TechProto costReference = LDB.techs.Select(CostReferenceTechnologyId);
            ItemProto exchanger = LDB.items.Select(EnergyExchangerItemId);
            ModelProto exchangerModel = exchanger != null ? LDB.models.Select(exchanger.ModelIndex) : null;
            if (vanillaResearch == null
                || costReference?.Items == null
                || costReference.ItemPoints == null
                || exchangerModel == null
                || LDB.items.Select(ProcessorItemId) == null)
            {
                throw new InvalidOperationException(
                    "Could not find the required research, Artificial Star, Energy Exchanger or Processor prototype.");
            }

            int gridIndex = Plugin.GetTabGridIndex(3, 2);
            ItemProto advanced = ProtoRegistry.RegisterItem(
                ItemId,
                NameKey,
                DescriptionKey,
                IconLoader.AdvancedEnergyExchanger,
                gridIndex,
                exchanger.StackSize,
                exchanger.Type);

            // Like belts and assemblers, either exchanger can be upgraded or downgraded in place to the other.
            int[] upgrades = { EnergyExchangerItemId, ItemId };
            exchanger.Grade = 1;
            exchanger.Upgrades = upgrades;
            advanced.Grade = 2;
            advanced.Upgrades = upgrades;

            ModelProto model = ProtoRegistry.RegisterModel(
                ModelId,
                advanced,
                exchangerModel.PrefabPath,
                null,
                exchanger.DescFields != null ? (int[])exchanger.DescFields.Clone() : Array.Empty<int>(),
                FindFreeBuildIndex(exchanger.BuildIndex));
            SubspaceLogisticsTechnology.RequireRegisteredId(model, ModelId);
            CopyModelData(exchangerModel, model);

            ProtoRegistry.RegisterRecipe(
                RecipeId,
                ERecipeType.Assemble,
                900,
                new[] { EnergyExchangerItemId, ProcessorItemId },
                new[] { 2, 4 },
                new[] { ItemId },
                new[] { 1 },
                DescriptionKey,
                TechnologyId,
                gridIndex,
                RecipeNameKey,
                IconLoader.AdvancedEnergyExchanger);

            TechProto technology = new TechProto
            {
                ID = TechnologyId,
                Name = TechNameKey,
                Desc = TechDescriptionKey,
                Conclusion = TechConclusionKey,
                IconPath = IconLoader.AdvancedEnergyExchangerTech,
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
            ProtoRegistry.onLoadingFinished += ConfigureModel;
            Logger.LogInfo("Registered the Advanced Energy Exchanger and Advanced Energy Engineering research.");
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

        // Runs once the model's prefab has loaded. PrefabDesc copies the prefab's material arrays, so tinting
        // them here leaves the vanilla Energy Exchanger untouched.
        private static void ConfigureModel()
        {
            ModelProto model = LDB.models.Select(ModelId);
            PrefabDesc desc = model != null ? model.prefabDesc : null;
            if (desc == null || !desc.isPowerExchanger)
            {
                Logger.LogError("The Advanced Energy Exchanger model did not load as an Energy Exchanger.");
                return;
            }

            desc.exchangeEnergyPerTick *= PowerMultiplier;
            WidenArms(model, desc);
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

        private static void WidenArms(ModelProto model, PrefabDesc desc)
        {
            for (int lod = 0; lod < desc.lodCount; lod++)
            {
                VertaBuffer verta = desc.lodVertas?[lod];
                if (verta?.data == null || verta.vertexSize < 9 || verta.frameCount < 2)
                {
                    continue;
                }

                bool[] moving = FindMovingVertices(verta);
                for (int frame = 0; frame < verta.frameCount; frame++)
                {
                    for (int vertex = 0; vertex < verta.vertexCount; vertex++)
                    {
                        if (!moving[vertex])
                        {
                            continue;
                        }

                        int i = (frame * verta.vertexCount + vertex) * verta.vertexSize;
                        Vector3 position = new Vector3(verta.data[i], verta.data[i + 1], verta.data[i + 2]);
                        Vector3 normal = new Vector3(verta.data[i + 3], verta.data[i + 4], verta.data[i + 5]);
                        Vector3 tangent = new Vector3(verta.data[i + 6], verta.data[i + 7], verta.data[i + 8]);
                        WidenVertex(ref position, ref normal, ref tangent);
                        verta.data[i] = position.x;
                        verta.data[i + 1] = position.y;
                        verta.data[i + 2] = position.z;
                        verta.data[i + 3] = normal.x;
                        verta.data[i + 4] = normal.y;
                        verta.data[i + 5] = normal.z;
                        verta.data[i + 6] = tangent.x;
                        verta.data[i + 7] = tangent.y;
                        verta.data[i + 8] = tangent.z;
                    }
                }

                verta.SetBufferData();

                // The mesh holds the rest pose; it sets the culling bounds and the pose before animation starts.
                Mesh mesh = desc.lodMeshes?[lod];
                if (mesh == null || mesh.vertexCount != verta.vertexCount)
                {
                    continue;
                }

                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                bool hasNormals = normals.Length == vertices.Length;
                bool hasTangents = tangents.Length == vertices.Length;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    if (!moving[vertex])
                    {
                        continue;
                    }

                    Vector3 normal = hasNormals ? normals[vertex] : Vector3.up;
                    Vector3 tangent = hasTangents ? (Vector3)tangents[vertex] : Vector3.right;
                    WidenVertex(ref vertices[vertex], ref normal, ref tangent);
                    if (hasNormals)
                    {
                        normals[vertex] = normal;
                    }

                    if (hasTangents)
                    {
                        tangents[vertex] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[vertex].w);
                    }
                }

                mesh.vertices = vertices;
                if (hasNormals)
                {
                    mesh.normals = normals;
                }

                if (hasTangents)
                {
                    mesh.tangents = tangents;
                }

                mesh.RecalculateBounds();
                if (lod == 0)
                {
                    model.meshBounds = mesh.bounds;
                }
            }
        }

        private static bool[] FindMovingVertices(VertaBuffer verta)
        {
            bool[] moving = new bool[verta.vertexCount];
            for (int frame = 1; frame < verta.frameCount; frame++)
            {
                for (int vertex = 0; vertex < verta.vertexCount; vertex++)
                {
                    if (moving[vertex])
                    {
                        continue;
                    }

                    int first = vertex * verta.vertexSize;
                    int current = (frame * verta.vertexCount + vertex) * verta.vertexSize;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if (Mathf.Abs(verta.data[current + axis] - verta.data[first + axis]) > 0.001f)
                        {
                            moving[vertex] = true;
                            break;
                        }
                    }
                }
            }

            return moving;
        }

        // Normals take the inverse scale so lighting follows the stretched surface.
        private static void WidenVertex(ref Vector3 position, ref Vector3 normal, ref Vector3 tangent)
        {
            float radius = new Vector2(position.x, position.z).magnitude;
            float angle = Mathf.Atan2(position.z, position.x) * Mathf.Rad2Deg;
            float diagonal = Mathf.Round((angle - 45f) / 90f) * 90f + 45f;
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HingeStartRadius, HingeEndRadius, radius))
                * Mathf.Clamp01(1f - Mathf.Abs(angle - diagonal) / ArmHalfAngle);
            if (weight <= 0f)
            {
                return;
            }

            float span = 1f + (ArmSpan - 1f) * weight;
            float breadth = 1f + (ArmBreadth - 1f) * weight;
            float radians = diagonal * Mathf.Deg2Rad;
            Vector2 outward = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 across = new Vector2(-outward.y, outward.x);

            position = Stretch(position, outward, across, span, breadth, HingeStartRadius);
            normal = Stretch(normal, outward, across, 1f / span, 1f / breadth, 0f).normalized;
            tangent = Stretch(tangent, outward, across, span, breadth, 0f).normalized;
        }

        private static Vector3 Stretch(
            Vector3 vector,
            Vector2 outward,
            Vector2 across,
            float outwardScale,
            float acrossScale,
            float origin)
        {
            Vector2 flat = new Vector2(vector.x, vector.z);
            float along = (Vector2.Dot(flat, outward) - origin) * outwardScale + origin;
            float side = Vector2.Dot(flat, across) * acrossScale;
            Vector2 result = outward * along + across * side;
            return new Vector3(result.x, vector.y, result.y);
        }

        // The build bar shows one item per index, so the item takes the first free slot in the exchanger's row.
        private static int FindFreeBuildIndex(int exchangerBuildIndex)
        {
            int row = exchangerBuildIndex / 100 * 100;
            for (int column = 1; column <= 10; column++)
            {
                int candidate = row + column;
                if (!LDB.items.dataArray.Any(item => item != null && item.BuildIndex == candidate))
                {
                    return candidate;
                }
            }

            Logger.LogWarning("No free build bar slot next to the Energy Exchanger; the Advanced Energy Exchanger is not on the build bar.");
            return 0;
        }

        internal static void LinkTechnologyTree()
        {
            SubspaceLogisticsTechnology.EnsureTechState(
                GameMain.data != null && GameMain.data.history != null
                    ? GameMain.data.history
                    : GameMain.history,
                TechnologyId);
        }
    }

    // Vanilla upgrades refresh assemblers, labs and consumers but not exchangers, which copy their power from the
    // prefab when built.
    [HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.UpgradeEntityWithComponents))]
    internal static class AdvancedEnergyExchangerUpgradePatch
    {
        private static void Postfix(PlanetFactory __instance, int entityId, ItemProto newProto)
        {
            if (entityId <= 0 || newProto?.prefabDesc == null || !newProto.prefabDesc.isPowerExchanger)
            {
                return;
            }

            int exchangerId = __instance.entityPool[entityId].powerExcId;
            PowerExchangerComponent[] pool = __instance.powerSystem?.excPool;
            if (exchangerId <= 0 || pool == null || exchangerId >= pool.Length || pool[exchangerId].id != exchangerId)
            {
                return;
            }

            pool[exchangerId].energyPerTick = newProto.prefabDesc.exchangeEnergyPerTick;
        }
    }
}
