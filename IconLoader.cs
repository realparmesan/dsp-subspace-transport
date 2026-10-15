using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SubspaceTransport
{
    internal static class IconLoader
    {
        private const string PathPrefix = "SubspaceTransport/Icons/";
        private const string ResourcePrefix = "SubspaceTransport.Icons.";

        internal const string WormholeLogisticsTech = PathPrefix + "wormhole_logistics_tech";
        internal const string WormholeEfficiencyTech = PathPrefix + "wormhole_efficiency_tech";
        internal const string BatterySphereEngineeringTech = PathPrefix + "battery_sphere_engineering_tech";
        internal const string SubspaceWarper = PathPrefix + "subspace_warper";
        internal const string BatteryRocket = PathPrefix + "battery_rocket";
        internal const string StellarHyperMassInducerTech = PathPrefix + "stellar_hyper_mass_inducer_tech";
        internal const string HyperMassRocket = PathPrefix + "hyper_mass_rocket";

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        internal static bool IsModIconPath(string path)
        {
            return path != null && path.StartsWith(PathPrefix, StringComparison.Ordinal);
        }

        internal static Sprite LoadSprite(string path)
        {
            Sprite sprite;
            if (Sprites.TryGetValue(path, out sprite))
            {
                return sprite;
            }

            string resourceName = ResourcePrefix + path.Substring(PathPrefix.Length) + ".png";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Plugin.ModLogger.LogError("Missing embedded icon '" + resourceName + "'.");
                    Sprites[path] = null;
                    return null;
                }

                byte[] bytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException("Could not read embedded icon '" + resourceName + "'.");
                    }

                    offset += read;
                }

                // PNGs load as ARGB32, matching the icon atlas format that IconSet copies into.
                Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!texture.LoadImage(bytes))
                {
                    Plugin.ModLogger.LogError("Could not decode embedded icon '" + resourceName + "'.");
                    Sprites[path] = null;
                    return null;
                }

                texture.name = path;
                texture.wrapMode = TextureWrapMode.Clamp;
                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                sprite.name = path;
                Sprites[path] = sprite;
                return sprite;
            }
        }
    }

    // Proto icons are loaded with Resources.Load<Sprite>(IconPath), so mod icon paths resolve to the embedded PNGs here.
    [HarmonyPatch(typeof(Resources), "Load", new[] { typeof(string), typeof(Type) })]
    internal static class IconResourcesLoadPatch
    {
        private static bool Prefix(string path, Type systemTypeInstance, ref UnityEngine.Object __result)
        {
            if (!IconLoader.IsModIconPath(path))
            {
                return true;
            }

            Sprite sprite = IconLoader.LoadSprite(path);
            if (systemTypeInstance == typeof(Texture2D) || systemTypeInstance == typeof(Texture))
            {
                __result = sprite != null ? sprite.texture : null;
            }
            else
            {
                __result = sprite;
            }

            return false;
        }
    }
}
