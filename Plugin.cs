using System.IO;
using BepInEx;
using HarmonyLib;
using crecheng.DSPModSave;
using xiaoye97;

namespace SubspaceTransport
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(LDBToolPlugin.MODGUID)]
    [BepInDependency(DSPModSavePlugin.MODGUID)]
    [BepInProcess("DSPGAME.exe")]
    [BepInProcess("Dyson Sphere Program.exe")]
    public sealed class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string PluginGuid = "realparmesan.dsp.subspacetransport";
        public const string PluginName = "Subspace Transport";
        public const string PluginVersion = "0.3.0";

        internal static Plugin Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = this;
            SubspaceLogisticsTechnology.Logger = Logger;
            LDBTool.PreAddDataAction += SubspaceLogisticsTechnology.Register;
            LDBTool.PostAddDataAction += SubspaceLogisticsTechnology.LinkTechnologyTree;

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            LDBTool.PreAddDataAction -= SubspaceLogisticsTechnology.Register;
            LDBTool.PostAddDataAction -= SubspaceLogisticsTechnology.LinkTechnologyTree;
            _harmony?.UnpatchSelf();
            if (Log == this)
            {
                Log = null;
            }
        }

        public void Export(BinaryWriter writer)
        {
            StationSettings.Export(writer);
        }

        public void Import(BinaryReader reader)
        {
            StationSettings.Import(reader);
        }

        public void IntoOtherSave()
        {
            StationSettings.Reset();
        }
    }
}
