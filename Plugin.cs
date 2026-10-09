using BepInEx;

namespace SubspaceTransport
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("DSPGAME.exe")]
    [BepInProcess("Dyson Sphere Program.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "realparmesan.dsp.subspacetransport";
        public const string PluginName = "Subspace Transport";
        public const string PluginVersion = "0.1.0";

        private void Awake()
        {
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }
    }
}
