using System.IO;
using BepInEx;
using CommonAPI;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using HarmonyLib;
using crecheng.DSPModSave;
using xiaoye97;

namespace SubspaceTransport
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(LDBToolPlugin.MODGUID)]
    [BepInDependency(DSPModSavePlugin.MODGUID)]
    [BepInDependency("dsp.common-api.CommonAPI")]
    [BepInDependency(MoreMegaStructureCompat.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [CommonAPISubmoduleDependency(nameof(ProtoRegistry), nameof(LocalizationModule), nameof(TabSystem))]
    [BepInProcess("DSPGAME.exe")]
    [BepInProcess("Dyson Sphere Program.exe")]
    public sealed class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string PluginGuid = "realparmesan.dsp.subspacetransport";
        public const string PluginName = "Wormhole Logistics";
        public const string PluginVersion = "0.7.2";

        private const string TabId = "WormholeLogistics:WormholeLogisticsTab";

        internal static Plugin Log { get; private set; }
        internal static BepInEx.Logging.ManualLogSource ModLogger { get; private set; }

        // Replicator and recipe-picker page that holds this mod's items.
        internal static int TabPage { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = this;
            ModLogger = Logger;
            SubspaceLogisticsTechnology.Logger = Logger;
            WormholeEfficiencyTechnology.Logger = Logger;
            BatterySphereContent.Logger = Logger;
            HyperMassContent.Logger = Logger;
            SubspaceRestockerContent.Logger = Logger;
            AdvancedEnergyExchangerContent.Logger = Logger;
            TabPage = TabSystem.RegisterTab(
                TabId,
                new TabData("Subspace", IconLoader.WormholeLogisticsTech));
            SubspaceLogisticsTechnology.RegisterTranslations();
            WormholeEfficiencyTechnology.RegisterTranslations();
            SubspaceWarperContent.RegisterTranslations();
            BatterySphereContent.RegisterTranslations();
            HyperMassContent.RegisterTranslations();
            SubspaceRestockerContent.RegisterTranslations();
            AdvancedEnergyExchangerContent.RegisterTranslations();
            LDBTool.PreAddDataAction += SubspaceLogisticsTechnology.Register;
            LDBTool.PreAddDataAction += WormholeEfficiencyTechnology.Register;
            LDBTool.PreAddDataAction += SubspaceWarperContent.Register;
            LDBTool.PreAddDataAction += BatterySphereContent.Register;
            LDBTool.PreAddDataAction += HyperMassContent.Register;
            LDBTool.PreAddDataAction += SubspaceRestockerContent.Register;
            LDBTool.PreAddDataAction += AdvancedEnergyExchangerContent.Register;
            LDBTool.PostAddDataAction += SubspaceLogisticsTechnology.LinkTechnologyTree;
            LDBTool.PostAddDataAction += WormholeEfficiencyTechnology.LinkTechnologyTree;
            LDBTool.PostAddDataAction += BatterySphereContent.LinkTechnologyTree;
            LDBTool.PostAddDataAction += HyperMassContent.LinkTechnologyTree;
            LDBTool.PostAddDataAction += SubspaceRestockerContent.LinkTechnologyTree;
            LDBTool.PostAddDataAction += AdvancedEnergyExchangerContent.LinkTechnologyTree;

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            MoreMegaStructureCompat.Initialize(_harmony);
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            LDBTool.PreAddDataAction -= SubspaceLogisticsTechnology.Register;
            LDBTool.PreAddDataAction -= WormholeEfficiencyTechnology.Register;
            LDBTool.PreAddDataAction -= SubspaceWarperContent.Register;
            LDBTool.PreAddDataAction -= BatterySphereContent.Register;
            LDBTool.PreAddDataAction -= HyperMassContent.Register;
            LDBTool.PreAddDataAction -= SubspaceRestockerContent.Register;
            LDBTool.PreAddDataAction -= AdvancedEnergyExchangerContent.Register;
            LDBTool.PostAddDataAction -= SubspaceLogisticsTechnology.LinkTechnologyTree;
            LDBTool.PostAddDataAction -= WormholeEfficiencyTechnology.LinkTechnologyTree;
            LDBTool.PostAddDataAction -= BatterySphereContent.LinkTechnologyTree;
            LDBTool.PostAddDataAction -= HyperMassContent.LinkTechnologyTree;
            LDBTool.PostAddDataAction -= SubspaceRestockerContent.LinkTechnologyTree;
            LDBTool.PostAddDataAction -= AdvancedEnergyExchangerContent.LinkTechnologyTree;
            _harmony?.UnpatchSelf();
            if (Log == this)
            {
                Log = null;
                ModLogger = null;
            }
        }

        internal static int GetTabGridIndex(int row, int column)
        {
            return TabPage * 1000 + row * 100 + column;
        }

        public void Export(BinaryWriter writer)
        {
            StationSettings.Export(writer);
            BatterySphereManager.Export(writer);
            HyperMassManager.Export(writer);
            SubspaceRestockerManager.Export(writer);
        }

        public void Import(BinaryReader reader)
        {
            TransportDiagnostics.Reset();
            StarInjectionAnimation.Reset();
            StationSettings.Import(reader);
            if (HasMoreData(reader))
            {
                BatterySphereManager.Import(reader);
            }
            else
            {
                BatterySphereManager.Reset();
            }

            // Saves from before the Hyper Mass Injector end after the Battery Sphere data.
            if (HasMoreData(reader))
            {
                HyperMassManager.Import(reader);
            }
            else
            {
                HyperMassManager.Reset();
            }

            // Saves from before the Subspace Restocker end after the Hyper Mass Injector data.
            if (HasMoreData(reader))
            {
                SubspaceRestockerManager.Import(reader);
            }
            else
            {
                SubspaceRestockerManager.Reset();
            }
        }

        private static bool HasMoreData(BinaryReader reader)
        {
            return reader.BaseStream.CanSeek && reader.BaseStream.Position < reader.BaseStream.Length;
        }

        public void IntoOtherSave()
        {
            TransportDiagnostics.Reset();
            StarInjectionAnimation.Reset();
            StationSettings.Reset();
            BatterySphereManager.Reset();
            HyperMassManager.Reset();
            SubspaceRestockerManager.Reset();
        }
    }
}
