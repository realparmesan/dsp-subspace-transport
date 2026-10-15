using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    // The mod's repeatable upgrades (Subspace Efficiency, Stellar Hyper Mass Amplification) live on the Upgrades
    // tab of the technology window: techs with an ID of 2000 and up are laid out there. This places them and adds
    // their numbers to the tab's tooltips and statistics panel.
    internal static class UpgradesTab
    {
        private const float RowSpacing = 4f;
        private const float ColumnSpacing = 4f;

        // The mod's techs sit in one new row under the vanilla upgrades, column 0 first.
        internal static Vector2 GetPosition(int column)
        {
            float minimumX = 0f;
            float maximumY = 0f;
            bool any = false;
            foreach (TechProto proto in LDB.techs.dataArray)
            {
                if (proto == null || proto.page != 1 || proto.ID == WormholeEfficiencyTechnology.TechnologyId
                    || proto.ID == HyperMassContent.AmplificationTechnologyId)
                {
                    continue;
                }

                minimumX = any ? Mathf.Min(minimumX, proto.Position.x) : proto.Position.x;
                maximumY = any ? Mathf.Max(maximumY, proto.Position.y) : proto.Position.y;
                any = true;
            }

            return new Vector2(minimumX + column * ColumnSpacing, maximumY + RowSpacing);
        }

        // What each level of an upgrade does, as the tooltip's "Unlocks" section lists it.
        internal static string GetLevelEffect(int technologyId)
        {
            if (technologyId == WormholeEfficiencyTechnology.TechnologyId)
            {
                return "Halves Subspace Transport station energy use per level";
            }

            if (technologyId == HyperMassContent.AmplificationTechnologyId)
            {
                return "+" + HyperMassContent.LuminosityPerLevel.ToString("0.0#") +
                    " L hyperinjected star luminosity per level";
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(TechProto), nameof(TechProto.UnlockFunctionText))]
    internal static class UpgradeLevelEffectPatch
    {
        private static void Postfix(TechProto __instance, ref string __result)
        {
            string effect = UpgradesTab.GetLevelEffect(__instance.ID);
            if (effect != null)
            {
                __result = string.IsNullOrEmpty(__result) ? effect : __result + "\r\n" + effect;
            }
        }
    }

    // Adds this mod's groups under the last group of the statistics panel's third page, by cloning that group
    // so they take on the game's fonts and layout.
    [HarmonyPatch(typeof(UITechTree), nameof(UITechTree.RefreshDataValueText))]
    internal static class UpgradeStatisticsPanelPatch
    {
        private const int StatisticsPage = 3;
        private const float UnlockedHeight = 54f;
        private const float LockedHeight = 34f;
        private const float GroupGap = 5f;

        private static readonly AccessTools.FieldRef<UITechTree, int> UpgradePage =
            AccessTools.FieldRefAccess<UITechTree, int>("upgradePage");

        private sealed class Group
        {
            internal RectTransform Root;
            internal Text Locked;
            internal GameObject Body;
            internal Text Title;
            internal Text Label;
            internal Text Value;
        }

        private static UITechTree _tree;
        private static Group[] _groups;
        private static bool _failed;

        private static void Postfix(UITechTree __instance)
        {
            if (_failed || UpgradePage(__instance) != StatisticsPage)
            {
                return;
            }

            try
            {
                if (_tree != __instance || _groups == null || _groups[0].Root == null)
                {
                    _groups = new[]
                    {
                        Create(__instance, "Subspace Transport"),
                        Create(__instance, "Stellar Hyper Mass")
                    };
                    _tree = __instance;
                }

                float top = __instance.rect3_6.anchoredPosition.y - __instance.rect3_6.sizeDelta.y - GroupGap;
                top = Show(
                    _groups[0],
                    top,
                    SubspaceLogisticsTechnology.IsUnlocked,
                    "Station energy use",
                    WormholeEfficiencyTechnology.EnergyMultiplier + "x vanilla");
                Show(
                    _groups[1],
                    top,
                    HyperMassContent.IsUnlocked,
                    "Hyperinjection luminosity",
                    HyperMassContent.TargetLuminosity.ToString("0.0#") + " L");
            }
            catch (Exception exception)
            {
                _failed = true;
                Plugin.ModLogger.LogError("Could not add the Subspace statistics to the Upgrades tab: " + exception);
            }
        }

        private static float Show(Group group, float top, bool unlocked, string label, string value)
        {
            float height = unlocked ? UnlockedHeight : LockedHeight;
            group.Root.anchoredPosition = new Vector2(group.Root.anchoredPosition.x, top);
            group.Root.sizeDelta = new Vector2(group.Root.sizeDelta.x, height);
            group.Locked.gameObject.SetActive(!unlocked);
            group.Body.SetActive(unlocked);
            if (unlocked)
            {
                if (group.Label != null)
                {
                    group.Label.text = label;
                }

                group.Value.text = value;
            }

            return top - height - GroupGap;
        }

        private static Group Create(UITechTree tree, string title)
        {
            RectTransform template = tree.rect3_6;
            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, template.parent);
            clone.name = "subspace-" + title.ToLowerInvariant().Replace(' ', '-');
            foreach (Localizer localizer in clone.GetComponentsInChildren<Localizer>(true))
            {
                UnityEngine.Object.DestroyImmediate(localizer);
            }

            // The clone's texts are in the same order as the template's.
            Text[] source = template.GetComponentsInChildren<Text>(true);
            Text[] copy = clone.GetComponentsInChildren<Text>(true);
            int lockedIndex = Array.IndexOf(source, tree.groupStat3_6);
            int valueIndex = Array.IndexOf(source, tree.dataValue3_6);
            if (lockedIndex < 0 || valueIndex < 0 || source.Length != copy.Length)
            {
                throw new InvalidOperationException("The statistics group has an unexpected layout.");
            }

            // Of the remaining texts, the topmost is the group's title and the next the stat label.
            List<Text> others = copy
                .Where((text, index) => index != lockedIndex && index != valueIndex)
                .OrderByDescending(text => text.transform.position.y)
                .ToList();
            Group group = new Group
            {
                Root = (RectTransform)clone.transform,
                Locked = copy[lockedIndex],
                Value = copy[valueIndex],
                Body = copy[valueIndex].transform.parent.gameObject,
                Title = others.Count > 0 ? others[0] : null,
                Label = others.Count > 1 ? others[1] : null
            };
            group.Locked.text = title;
            if (group.Title != null)
            {
                group.Title.text = title;
            }

            for (int i = 2; i < others.Count; i++)
            {
                others[i].text = "";
            }

            return group;
        }
    }
}
