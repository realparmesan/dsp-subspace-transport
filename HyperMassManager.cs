using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SubspaceTransport
{
    // Hyper Mass Injector projects. A planned star is built by Hyper Mass Rockets like a Dyson Sphere; once its
    // sphere generates ActivationWatts the player can hyperinject it, turning the star into a hypergiant whose
    // luminosity follows the Stellar Hyper Mass research. Star data is regenerated from the seed on every load, so
    // the conversion is stored here and reapplied after loading.
    internal static class HyperMassManager
    {
        internal const long ActivationWatts = 1000000L;

        private const int SaveFormatVersion = 2;
        private const int MaximumSavedStars = 10000;
        private const long TicksPerSecond = 60;

        private sealed class StarProject
        {
            internal bool Planned;

            // The luminosity the star was hyperinjected to, or 0 if it hasn't been.
            internal float InjectedLuminosity;

            // What the star becomes: a hypergiant, or a black hole.
            internal bool BlackHole;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, StarProject> Projects = new Dictionary<int, StarProject>();

        internal static bool IsPlanned(int starIndex)
        {
            lock (Sync)
            {
                StarProject project;
                return Projects.TryGetValue(starIndex, out project) && project.Planned;
            }
        }

        internal static float GetInjectedLuminosity(int starIndex)
        {
            lock (Sync)
            {
                StarProject project;
                return Projects.TryGetValue(starIndex, out project) ? project.InjectedLuminosity : 0f;
            }
        }

        internal static bool IsBlackHoleTarget(int starIndex)
        {
            lock (Sync)
            {
                StarProject project;
                return Projects.TryGetValue(starIndex, out project) && project.BlackHole;
            }
        }

        // The choice is fixed once the star has been hyperinjected.
        internal static bool CanChooseTarget(int starIndex)
        {
            return IsPlanned(starIndex) && GetInjectedLuminosity(starIndex) <= 0f;
        }

        internal static void SetBlackHoleTarget(int starIndex, bool blackHole)
        {
            lock (Sync)
            {
                StarProject project;
                if (Projects.TryGetValue(starIndex, out project) && project.InjectedLuminosity <= 0f)
                {
                    project.BlackHole = blackHole;
                }
            }
        }

        internal static void SetPlanned(int starIndex, bool planned)
        {
            if (starIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(starIndex));
            }

            lock (Sync)
            {
                StarProject project;
                if (!Projects.TryGetValue(starIndex, out project))
                {
                    if (!planned)
                    {
                        return;
                    }

                    project = new StarProject();
                    Projects.Add(starIndex, project);
                }

                project.Planned = planned;

                // A hyperinjected star stays converted whatever the star is planned as later.
                if (!planned && project.InjectedLuminosity <= 0f)
                {
                    Projects.Remove(starIndex);
                }
            }
        }

        internal static string ActivationPower
        {
            get { return FormatPower(ActivationWatts); }
        }

        internal static string FormatPower(long watts)
        {
            return watts >= 1000000000L
                ? (watts / 1e9).ToString("0.##") + " GW"
                : (watts / 1e6).ToString("0.##") + " MW";
        }

        internal static long GetSphereWatts(int starIndex)
        {
            DysonSphere sphere = GetSphere(starIndex);
            return sphere != null ? sphere.energyGenCurrentTick * TicksPerSecond : 0L;
        }

        // Null when the star can be hyperinjected now; otherwise why not, for the editor.
        internal static string GetInjectionBlocker(StarData star)
        {
            if (star == null)
            {
                return "No star selected.";
            }

            if (!HyperMassContent.IsUnlocked)
            {
                return "Research Stellar Hyper Mass Inducer first.";
            }

            if (!IsPlanned(star.index))
            {
                return "Plan a Hyper Mass Injector at this star first.";
            }

            if (!StarConversion.CanConvert(star))
            {
                return "Only main-sequence and giant stars can be hyperinjected.";
            }

            if (GetSphereWatts(star.index) < ActivationWatts)
            {
                return "The Hyper Mass Injector sphere must generate " + FormatPower(ActivationWatts) + ".";
            }

            float injected = GetInjectedLuminosity(star.index);
            if (injected > 0f && injected >= HyperMassContent.TargetLuminosity - 0.001f)
            {
                return "Already hyperinjected to " + injected.ToString("0.0#") +
                    " L. Research Stellar Hyper Mass Amplification to go further.";
            }

            return null;
        }

        internal static void Inject(StarData star)
        {
            if (GetInjectionBlocker(star) != null)
            {
                return;
            }

            float target = HyperMassContent.TargetLuminosity;
            bool blackHole;
            lock (Sync)
            {
                Projects[star.index].InjectedLuminosity = target;
                blackHole = Projects[star.index].BlackHole;
            }

            // The injector sphere is consumed by the conversion. The star is converted with no sphere around it, so
            // nothing limits its growth, and the empty replacement is created from the converted star.
            // The conversion lands at the animation's core flash; the saved project already records it, so a save
            // made before then converts the star on load.
            DestroySphere(star.index);
            StarInjectionAnimation.Begin(star, () => Convert(star, target, blackHole));
        }

        private static void Convert(StarData star, float target, bool blackHole)
        {
            // A sphere created since the injection began (opening the Dyson editor makes one, with a default swarm
            // orbit) would cap the star's growth, so it goes too before the star is converted.
            DestroySphere(star.index);
            StarConversion.Apply(star, target, blackHole);
            DysonSphere replacement = GameMain.data.CreateDysonSphere(star.index);
            if (replacement != null)
            {
                StarConversion.UpdateSphere(star);

                // An editor showing this star was left holding no sphere; it takes the replacement.
                UIDysonEditor editor = GetEditor();
                if (editor?.selection != null && editor.selection.viewStar == star)
                {
                    editor.selection.viewDysonSphere = replacement;
                    StarConversion.RefreshEditor(star);
                }
            }

            Plugin.ModLogger.LogInfo(
                "Hyperinjected " + star.displayName + " into a " + (blackHole ? "black hole" : "hypergiant") +
                " at " + star.dysonLumino.ToString("0.000") + " L.");
        }

        // Reapplies every saved conversion to the freshly generated galaxy.
        internal static void ApplyConversions()
        {
            GalaxyData galaxy = GameMain.data != null ? GameMain.data.galaxy : null;
            if (galaxy?.stars == null)
            {
                return;
            }

            List<KeyValuePair<int, StarProject>> conversions = new List<KeyValuePair<int, StarProject>>();
            lock (Sync)
            {
                foreach (KeyValuePair<int, StarProject> entry in Projects)
                {
                    if (entry.Value.InjectedLuminosity > 0f && entry.Key < galaxy.starCount)
                    {
                        conversions.Add(entry);
                    }
                }
            }

            foreach (KeyValuePair<int, StarProject> conversion in conversions)
            {
                StarData star = galaxy.stars[conversion.Key];
                if (star != null && StarConversion.CanConvert(star))
                {
                    StarConversion.Apply(star, conversion.Value.InjectedLuminosity, conversion.Value.BlackHole);
                }
            }
        }

        internal static void Export(BinaryWriter writer)
        {
            lock (Sync)
            {
                writer.Write(SaveFormatVersion);
                writer.Write(Projects.Count);
                foreach (KeyValuePair<int, StarProject> entry in Projects)
                {
                    writer.Write(entry.Key);
                    writer.Write(entry.Value.Planned);
                    writer.Write(entry.Value.InjectedLuminosity);
                    writer.Write(entry.Value.BlackHole);
                }
            }
        }

        internal static void Import(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version < 1 || version > SaveFormatVersion)
            {
                throw new InvalidDataException("Unsupported Hyper Mass Injector save format: " + version + ".");
            }

            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumSavedStars)
            {
                throw new InvalidDataException("Invalid Hyper Mass Injector project count: " + count + ".");
            }

            Dictionary<int, StarProject> loadedProjects = new Dictionary<int, StarProject>();
            for (int i = 0; i < count; i++)
            {
                int starIndex = reader.ReadInt32();
                bool planned = reader.ReadBoolean();
                float luminosity = reader.ReadSingle();
                bool blackHole = version >= 2 && reader.ReadBoolean();
                if (starIndex < 0
                    || float.IsNaN(luminosity)
                    || luminosity < 0f
                    || luminosity > HyperMassContent.MaximumLuminosity + 1f)
                {
                    throw new InvalidDataException("Invalid Hyper Mass Injector project data.");
                }

                loadedProjects[starIndex] = new StarProject { Planned = planned, InjectedLuminosity = luminosity, BlackHole = blackHole };
            }

            lock (Sync)
            {
                Projects.Clear();
                foreach (KeyValuePair<int, StarProject> entry in loadedProjects)
                {
                    Projects.Add(entry.Key, entry.Value);
                }
            }

            ApplyConversions();
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                Projects.Clear();
            }
        }

        private static void DestroySphere(int starIndex)
        {
            DysonSphere sphere = GetSphere(starIndex);
            if (sphere == null)
            {
                return;
            }

            // The Dyson editor draws the sphere it holds every frame, so it must let go of a freed one.
            UIDysonEditor editor = GetEditor();
            if (editor?.selection != null && editor.selection.viewDysonSphere == sphere)
            {
                editor.selection.viewDysonSphere = null;
            }

            GameMain.data.dysonSpheres[starIndex] = null;
            sphere.Free();
        }

        private static UIDysonEditor GetEditor()
        {
            return UIRoot.instance != null && UIRoot.instance.uiGame != null ? UIRoot.instance.uiGame.dysonEditor : null;
        }

        internal static DysonSphere GetSphere(int starIndex)
        {
            DysonSphere[] spheres = GameMain.data != null ? GameMain.data.dysonSpheres : null;
            return spheres != null && starIndex >= 0 && starIndex < spheres.Length ? spheres[starIndex] : null;
        }
    }

    // Changes a star's data, its system and its Dyson Sphere for a hyperinjection. Each star's generated values are
    // kept the first time it is converted, so converting again (after more research, or on load) never compounds.
    internal static class StarConversion
    {
        // Radius in solar radii: a blue hypergiant at the base luminosity, growing to a violet one at full
        // Amplification.
        private const float BlueRadius = 10f;
        private const float VioletRadius = 20f;

        // A black hole's event horizon and accretion disc, in solar radii.
        private const float BlackHoleRadius = 1.5f;

        // The star's visible surface may reach this fraction of the innermost planet's orbit.
        private const float OrbitClearance = 0.8f;

        private static readonly Color HyperBlue = new Color(0.35f, 0.58f, 1f, 1f);
        private static readonly Color HyperViolet = new Color(0.66f, 0.3f, 1f, 1f);

        private static readonly AccessTools.FieldRef<StarSimulator, Color> OriginalFlareColor =
            AccessTools.FieldRefAccess<StarSimulator, Color>("originalSunFlareColor");

        private sealed class Snapshot
        {
            internal readonly float Mass;
            internal readonly float Temperature;
            internal readonly float Luminosity;
            internal readonly float Radius;
            internal readonly float HabitableRadius;
            internal readonly float LightBalanceRadius;

            internal Snapshot(StarData star)
            {
                Mass = star.mass;
                Temperature = star.temperature;
                Luminosity = star.luminosity;
                Radius = star.radius;
                HabitableRadius = star.habitableRadius;
                LightBalanceRadius = star.lightBalanceRadius;
            }

            internal float DysonLumino
            {
                get { return Mathf.Round((float)Math.Pow(Luminosity, 0.33) * 1000f) / 1000f; }
            }
        }

        private static readonly ConditionalWeakTable<StarData, Snapshot> Originals =
            new ConditionalWeakTable<StarData, Snapshot>();

        internal static bool CanConvert(StarData star)
        {
            return star != null
                && (star.type == EStarType.MainSeqStar || star.type == EStarType.GiantStar || IsConverted(star));
        }

        // Converted stars that are hypergiants rather than black holes; only these get the blue-violet tint.
        internal static bool IsHypergiant(StarData star)
        {
            return IsConverted(star) && star.type != EStarType.BlackHole;
        }

        internal static bool IsConverted(StarData star)
        {
            Snapshot snapshot;
            return star != null && Originals.TryGetValue(star, out snapshot);
        }

        // The star's mass as generated, before any hyperinjection.
        internal static float GetOriginalMass(StarData star)
        {
            Snapshot snapshot;
            return star != null && Originals.TryGetValue(star, out snapshot) ? snapshot.Mass : star.mass;
        }

        // 0 for a blue hypergiant at the base luminosity, 1 for a violet one at full Amplification.
        internal static float GetVioletness(StarData star)
        {
            return Mathf.InverseLerp(
                HyperMassContent.BaseLuminosity,
                HyperMassContent.MaximumLuminosity,
                star.dysonLumino);
        }

        internal static void Apply(StarData star, float targetLumino, bool blackHole)
        {
            Snapshot original = Originals.GetValue(star, s => new Snapshot(s));

            // Never makes a star dimmer than it was generated, so a bright O-type star keeps its own luminosity.
            float lumino = Mathf.Max(targetLumino, original.DysonLumino);
            star.luminosity = (float)Math.Pow(lumino, 1.0 / 0.33);
            float luminosityRatio = star.luminosity / Mathf.Max(original.Luminosity, 0.0001f);

            // StarGen: luminosity = mass^0.7 on the main sequence, and temperature follows mass.
            star.mass = Mathf.Max(original.Mass, Mathf.Pow(star.luminosity, 1f / 0.7f));
            star.temperature = Mathf.Max(original.Temperature, TemperatureForMass(star.mass));
            // Hypergiants are a giant-star class of their own: main-sequence stars change class, and the giant
            // materials give the star its swollen look in space and on the star map.
            star.type = blackHole ? EStarType.BlackHole : EStarType.GiantStar;
            star.spectr = blackHole ? ESpectrType.X : ESpectrType.O;
            star.classFactor = blackHole ? 0f : 2f;
            star.color = 1f;
            star.lightBalanceRadius = original.LightBalanceRadius * Mathf.Sqrt(luminosityRatio);
            star.habitableRadius = original.HabitableRadius * Mathf.Sqrt(luminosityRatio);

            float violetness = GetVioletness(star);
            float desiredRadius = blackHole ? BlackHoleRadius : Mathf.Lerp(BlueRadius, VioletRadius, violetness);
            star.radius = Mathf.Min(desiredRadius, GetRadiusLimit(star));
            if (!blackHole)
            {
                star.radius = Mathf.Max(original.Radius, star.radius);
            }

            // Sphere layers default to dysonRadius, which must stay outside the bigger star.
            star.dysonRadius = Mathf.Max(
                star.dysonRadius,
                (float)((double)(star.physicsRadius * 1.5f) / 40000.0));

            UpdatePlanets(star);
            UpdateSphere(star);
            RefreshSimulator(star);
            RefreshStarmap(star);
            RefreshEditor(star);
        }

        private static float TemperatureForMass(float mass)
        {
            return (float)(Math.Pow(mass, 0.56 + 0.14 / (Math.Log10(mass + 4f) / Math.Log10(5.0))) * 4450.0 + 1300.0);
        }

        // The star must stay clear of its planets and of any Dyson Sphere layer or swarm orbit already built.
        // Returns a radius in solar radii.
        private static float GetRadiusLimit(StarData star)
        {
            float limit = float.MaxValue;
            if (star.planets != null)
            {
                foreach (PlanetData planet in star.planets)
                {
                    if (planet != null && planet.orbitAround == 0)
                    {
                        limit = Mathf.Min(limit, planet.orbitRadius * 40000f * OrbitClearance / StarData.kViewRadiusRatio);
                    }
                }
            }

            DysonSphere sphere = HyperMassManager.GetSphere(star.index);
            if (sphere != null)
            {
                if (sphere.layersIdBased != null)
                {
                    foreach (DysonSphereLayer layer in sphere.layersIdBased)
                    {
                        if (layer != null && layer.id > 0)
                        {
                            limit = Mathf.Min(limit, layer.orbitRadius / 1.5f / StarData.kPhysicsRadiusRatio);
                        }
                    }
                }

                if (sphere.swarm?.orbits != null)
                {
                    foreach (SailOrbit orbit in sphere.swarm.orbits)
                    {
                        if (orbit.id > 0)
                        {
                            limit = Mathf.Min(limit, orbit.radius / 1.5f / StarData.kPhysicsRadiusRatio);
                        }
                    }
                }
            }

            return limit;
        }

        // PlanetGen's solar luminosity, so solar panels in the system follow the brighter star.
        private static void UpdatePlanets(StarData star)
        {
            if (star.planets == null)
            {
                return;
            }

            foreach (PlanetData planet in star.planets)
            {
                if (planet == null)
                {
                    continue;
                }

                float luminosity = Mathf.Pow(star.lightBalanceRadius / (planet.sunDistance + 0.01f), 0.6f);
                if (luminosity > 1f)
                {
                    luminosity = Mathf.Log(luminosity) + 1f;
                    luminosity = Mathf.Log(luminosity) + 1f;
                    luminosity = Mathf.Log(luminosity) + 1f;
                }

                planet.luminosity = Mathf.Round(luminosity * 100f) / 100f;
            }
        }

        // DysonSphere.Init reads the star once, so a sphere that already exists takes the new output here.
        internal static void UpdateSphere(StarData star)
        {
            DysonSphere sphere = HyperMassManager.GetSphere(star.index);
            if (sphere == null)
            {
                return;
            }

            double lumino = star.dysonLumino;
            sphere.energyGenPerSail = (long)(Configs.freeMode.solarSailEnergyPerTick * lumino);
            sphere.energyGenPerNode = (long)(Configs.freeMode.dysonNodeEnergyPerTick * lumino);
            sphere.energyGenPerFrame = (long)(Configs.freeMode.dysonFrameEnergyPerTick * lumino);
            sphere.energyGenPerShell = (long)(Configs.freeMode.dysonShellEnergyPerTick * lumino);
            sphere.gravity = (float)(86646732.73933044 * star.mass) * 4f;
            sphere.minOrbitRadius = Mathf.Max(
                sphere.minOrbitRadius,
                Mathf.Ceil(star.physicsRadius * 1.5f / 100f) * 100f);
            if (sphere.maxOrbitRadius < sphere.minOrbitRadius)
            {
                sphere.maxOrbitRadius = sphere.minOrbitRadius;
            }

            Color hue = GetHue(star);
            sphere.sunColor = Tint(Configs.builtin.dysonSphereSunColors.Evaluate(star.color), hue, 0.7f);
            sphere.emissionColor = Tint(Configs.builtin.dysonSphereEmissionColors.Evaluate(star.color), hue, 0.7f);
            if (sphere.swarm != null)
            {
                sphere.swarm.sailFarMaterial?.SetColor("_SunColor", sphere.sunColor);
                sphere.swarm.sailNearMaterial?.SetColor("_SunColor", sphere.sunColor);
            }
        }

        // The star map builds its star objects once, so rebuild the converted star's object from the new data.
        private static void RefreshStarmap(StarData star)
        {
            UIStarmap starmap = UIRoot.instance != null && UIRoot.instance.uiGame != null
                ? UIRoot.instance.uiGame.starmap
                : null;
            if (starmap?.starUIs == null || star.index >= starmap.starUIs.Length)
            {
                return;
            }

            UIStarmapStarObject starObject = starmap.starUIs[star.index]?.starObject;
            if (starObject == null || !starObject.inited)
            {
                return;
            }

            bool wasActive = starObject.active;
            starObject._Free();
            starObject._Init(star);
            if (wasActive)
            {
                starObject._Open();
            }
        }

        // The Dyson Sphere editor sizes and colours its star only when its view star changes, so a conversion made
        // while the editor already shows that star needs the editor to run that step again.
        internal static void RefreshEditor(StarData star)
        {
            UIDysonEditor editor = UIRoot.instance != null && UIRoot.instance.uiGame != null
                ? UIRoot.instance.uiGame.dysonEditor
                : null;
            if (editor?.selection == null || editor.selection.viewStar != star)
            {
                return;
            }

            AccessTools.Method(typeof(UIDysonEditor), "OnViewStarChange")?.Invoke(editor, null);
        }

        internal static void TintEditorStar(UIDysonEditor editor)
        {
            StarData star = editor.selection != null ? editor.selection.viewStar : null;
            if (!IsHypergiant(star))
            {
                return;
            }

            TintMaterial(EditorStarMaterial(editor), editor.starMats, GetHue(star));
        }

        private static readonly AccessTools.FieldRef<UIDysonEditor, Material> EditorStarMaterial =
            AccessTools.FieldRefAccess<UIDysonEditor, Material>("starMaterial");

        internal static void TintStarmapObject(UIStarmapStarObject starObject)
        {
            StarData star = starObject.star;
            if (!IsHypergiant(star))
            {
                return;
            }

            Color hue = GetHue(star);
            TintMaterial(StarObjectSpotMaterial(starObject), starObject.spotBlender, hue);
            TintMaterial(StarObjectMassMaterial(starObject), starObject.massBlender, hue);
        }

        private static readonly AccessTools.FieldRef<UIStarmapStarObject, Material> StarObjectSpotMaterial =
            AccessTools.FieldRefAccess<UIStarmapStarObject, Material>("spotMat");

        private static readonly AccessTools.FieldRef<UIStarmapStarObject, Material> StarObjectMassMaterial =
            AccessTools.FieldRefAccess<UIStarmapStarObject, Material>("massMat");

        private static void TintMaterial(Material material, StarMaterialBlender blender, Color hue)
        {
            if (material == null || blender?.colorPropNames == null)
            {
                return;
            }

            foreach (string property in blender.colorPropNames)
            {
                if (material.HasProperty(property))
                {
                    material.SetColor(property, Tint(material.GetColor(property), hue, 0.85f));
                }
            }
        }

        internal static void RefreshSimulator(StarData star)
        {
            StarSimulator[] simulators = GameMain.universeSimulator != null
                ? GameMain.universeSimulator.starSimulators
                : null;
            if (simulators == null || star.index >= simulators.Length || simulators[star.index] == null)
            {
                return;
            }

            // SetStarData only reads the colour parameter once; clear it so the bluer spectrum is picked up.
            // TintSimulator then runs from the SetStarData postfix.
            StarSimulator simulator = simulators[star.index];
            simulator.sunColorParam = -1f;
            simulator.SetStarData(star);
        }

        internal static void TintSimulator(StarSimulator simulator)
        {
            StarData star = simulator.starData;
            if (!IsHypergiant(star))
            {
                return;
            }

            Color hue = GetHue(star);
            TintMaterialColor(simulator.bodyRenderer, "_SunCenterColor", hue, 0.85f);
            TintMaterialColor(simulator.bodyRenderer, "_SunGlowColor", hue, 0.85f);
            if (simulator.haloRenderer != null && simulator.haloRenderer.sharedMaterial != null)
            {
                simulator.haloRenderer.sharedMaterial.color =
                    Tint(simulator.haloRenderer.sharedMaterial.color, hue, 0.85f);
            }

            TintBlendedMaterial(simulator.massRenderer, simulator.massMatBlender, hue);
            TintBlendedMaterial(simulator.atmosRenderer, simulator.atmoMatBlender, hue);
            TintBlendedMaterial(simulator.effectRenderer, simulator.effectMatBlender, hue);

            simulator.sunLight.color = Tint(simulator.sunLight.color, hue, 0.6f);
            simulator.sunLightColorInSpace = Tint(simulator.sunLightColorInSpace, hue, 0.6f);
            simulator.sunAtmosColor = Tint(simulator.sunAtmosColor, hue, 0.6f);
            simulator.sunriseAtmosColor = Tint(simulator.sunriseAtmosColor, hue, 0.6f);
            simulator.sunsetColor0 = Tint(simulator.sunsetColor0, hue, 0.5f);
            simulator.sunsetColor1 = Tint(simulator.sunsetColor1, hue, 0.5f);
            simulator.sunsetColor2 = Tint(simulator.sunsetColor2, hue, 0.5f);
            if (simulator.sunFlare != null)
            {
                Color flare = Tint(OriginalFlareColor(simulator), hue, 0.8f);
                OriginalFlareColor(simulator) = flare;
                simulator.sunFlare.color = flare;
            }
        }

        private static void TintMaterialColor(Renderer renderer, string property, Color hue, float strength)
        {
            Material material = renderer != null ? renderer.sharedMaterial : null;
            if (material != null && material.HasProperty(property))
            {
                material.SetColor(property, Tint(material.GetColor(property), hue, strength));
            }
        }

        private static void TintBlendedMaterial(Renderer renderer, StarMaterialBlender blender, Color hue)
        {
            if (blender?.colorPropNames == null)
            {
                return;
            }

            foreach (string property in blender.colorPropNames)
            {
                TintMaterialColor(renderer, property, hue, 0.85f);
            }
        }

        internal static Color GetHue(StarData star)
        {
            return Color.Lerp(HyperBlue, HyperViolet, GetVioletness(star));
        }

        // Moves a colour toward the hue while keeping its brightness and alpha, so HDR glow colours stay bright.
        private static Color Tint(Color color, Color hue, float strength)
        {
            float brightness = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            Color tinted = hue * brightness;
            tinted.a = color.a;
            return Color.Lerp(color, tinted, strength);
        }
    }

    [HarmonyPatch(typeof(StarSimulator), nameof(StarSimulator.SetStarData))]
    internal static class HyperMassStarSimulatorPatch
    {
        private static void Postfix(StarSimulator __instance)
        {
            StarConversion.TintSimulator(__instance);
        }
    }

    [HarmonyPatch(typeof(UIDysonEditor), "OnViewStarChange")]
    internal static class HyperMassEditorStarPatch
    {
        private static void Postfix(UIDysonEditor __instance)
        {
            StarConversion.TintEditorStar(__instance);
        }
    }

    [HarmonyPatch(typeof(UIStarmapStarObject), "_OnInit")]
    internal static class HyperMassStarmapStarPatch
    {
        private static void Postfix(UIStarmapStarObject __instance)
        {
            StarConversion.TintStarmapObject(__instance);
        }
    }

    [HarmonyPatch(typeof(StarData), nameof(StarData.typeString), MethodType.Getter)]
    internal static class HyperMassStarTypePatch
    {
        private static void Postfix(StarData __instance, ref string __result)
        {
            if (StarConversion.IsHypergiant(__instance))
            {
                __result = StarConversion.GetVioletness(__instance) < 0.5f ? "Blue Hypergiant" : "Violet Hypergiant";
            }
        }
    }

    // The mecha's star gravity scales with mass, and sailing near a star spends energy to hold against it. A
    // hypergiant's raised mass would pull hard enough to drain the mecha and outrun its thrust, so the mecha feels
    // the star's original mass instead.
    [HarmonyPatch(typeof(PlayerController), "ApplyGravity")]
    internal static class HypergiantMechaGravityPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo mass = AccessTools.Field(typeof(StarData), "mass");
            MethodInfo originalMass = AccessTools.Method(typeof(StarConversion), nameof(StarConversion.GetOriginalMass));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(mass))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = originalMass;
                    replaced++;
                }

                yield return instruction;
            }

            if (replaced == 0)
            {
                Plugin.ModLogger?.LogError("Hypergiant gravity patch: could not find the star mass in ApplyGravity.");
            }
        }
    }
    // The game's star value check flags giants above 60 M and stars brighter than their mass allows, which is what a
    // hyperinjected star is. Its own check is repeated here for every other star, and converted stars are skipped.
    [HarmonyPatch(typeof(ABN_StarValue), "OnGameTick")]
    internal static class HypergiantAbnormalityPatch
    {
        private static bool Prefix(ABN_StarValue __instance, long time)
        {
            int checkInterval = Traverse.Create(__instance).Field("checkInterval").GetValue<int>();
            if (time % checkInterval != __instance.protoId % checkInterval)
            {
                return false;
            }

            DysonSphere[] spheres = GameMain.data.dysonSpheres;
            for (int i = 0; i < spheres.Length; i++)
            {
                if (spheres[i] == null || StarConversion.IsConverted(spheres[i].starData))
                {
                    continue;
                }

                StarData star = spheres[i].starData;
                float allowed = 1f;
                if (star.type == EStarType.GiantStar || star.type == EStarType.MainSeqStar)
                {
                    if (star.mass > 60f)
                    {
                        __instance.abnormalData.TriggerAbnormality(__instance.protoId, 0, 0L, star.id, (long)star.type, (long)(star.mass * 1000f), (long)(star.luminosity * 1000f), 0L);
                    }

                    allowed = Mathf.Pow(star.mass, 0.7f) * (star.type == EStarType.GiantStar ? 1.6f : 1f) * 1.1f;
                }

                if (star.luminosity > allowed)
                {
                    __instance.abnormalData.TriggerAbnormality(__instance.protoId, 0, 1L, star.id, (long)star.type, (long)(star.mass * 1000f), (long)(star.luminosity * 1000f), (long)allowed);
                    break;
                }
            }

            return false;
        }
    }
}
