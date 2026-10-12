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
    [CommonAPISubmoduleDependency(nameof(ProtoRegistry), nameof(LocalizationModule))]
    [BepInProcess("DSPGAME.exe")]
    [BepInProcess("Dyson Sphere Program.exe")]
    public sealed class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string PluginGuid = "realparmesan.dsp.subspacetransport";
        public const string PluginName = "Wormhole Logistics";
        public const string PluginVersion = "0.5.3";

        internal static Plugin Log { get; private set; }
        internal static BepInEx.Logging.ManualLogSource ModLogger { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = this;
            ModLogger = Logger;
            SubspaceLogisticsTechnology.Logger = Logger;
            BatterySphereContent.Logger = Logger;
            LDBTool.PreAddDataAction += SubspaceLogisticsTechnology.Register;
            LDBTool.PreAddDataAction += HyperdriveModuleContent.Register;
            LDBTool.PreAddDataAction += BatterySphereContent.Register;
            LDBTool.PostAddDataAction += SubspaceLogisticsTechnology.LinkTechnologyTree;
            LDBTool.PostAddDataAction += BatterySphereContent.LinkTechnologyTree;

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            LDBTool.PreAddDataAction -= SubspaceLogisticsTechnology.Register;
            LDBTool.PreAddDataAction -= HyperdriveModuleContent.Register;
            LDBTool.PreAddDataAction -= BatterySphereContent.Register;
            LDBTool.PostAddDataAction -= SubspaceLogisticsTechnology.LinkTechnologyTree;
            LDBTool.PostAddDataAction -= BatterySphereContent.LinkTechnologyTree;
            _harmony?.UnpatchSelf();
            if (Log == this)
            {
                Log = null;
                ModLogger = null;
            }
        }

        public void Export(BinaryWriter writer)
        {
            StationSettings.Export(writer);
            BatterySphereManager.Export(writer);
        }

        public void Import(BinaryReader reader)
        {
            StationSettings.Import(reader);
            if (reader.BaseStream.CanSeek && reader.BaseStream.Position < reader.BaseStream.Length)
            {
                BatterySphereManager.Import(reader);
            }
            else
            {
                BatterySphereManager.Reset();
            }
        }

        public void IntoOtherSave()
        {
            StationSettings.Reset();
            BatterySphereManager.Reset();
        }
    }
}
