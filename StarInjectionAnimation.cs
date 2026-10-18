using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SubspaceTransport
{
    // Plays out a hyperinjection the way a star that suddenly gains mass would respond. The mass falling in drives
    // the star out of thermal equilibrium: its surface boils and is compressed, the core flares and blows off a shell
    // of ejecta, the envelope swells past its new size and cools redder, then it contracts on its thermal timescale,
    // in damped pulses, to the hotter, bluer and brighter star it now is. The conversion itself lands at the flash.
    //
    // Only the view is animated: star sizes and colours are scaled on top of what the game sets each tick, and
    // everything is restored at the end. The clock is game ticks, so the animation pauses with the game.
    internal static class StarInjectionAnimation
    {
        private const float TicksPerSecond = 60f;
        private const float FlashAt = 3f;
        private const float ReboundEnd = 3.5f;
        private const float SwellEnd = 9f;
        private const float End = 16f;

        // The swollen envelope peaks at this multiple of the final radius before settling.
        private const float SwellOvershoot = 1.3f;
        private const float PeakBrightness = 7f;

        private static readonly Color Warm = new Color(1f, 0.45f, 0.25f, 1f);

        private static readonly AccessTools.FieldRef<StarSimulator, Material> BodyMaterial =
            AccessTools.FieldRefAccess<StarSimulator, Material>("bodyMaterial");
        private static readonly AccessTools.FieldRef<StarSimulator, Material> HaloMaterial =
            AccessTools.FieldRefAccess<StarSimulator, Material>("haloMaterial");
        private static readonly AccessTools.FieldRef<StarSimulator, Material> AtmoMaterial =
            AccessTools.FieldRefAccess<StarSimulator, Material>("atmoMaterial");
        private static readonly AccessTools.FieldRef<StarSimulator, Material> EffectMaterial =
            AccessTools.FieldRefAccess<StarSimulator, Material>("effectMaterial");
        private static readonly AccessTools.FieldRef<UIDysonEditor, Material> EditorStarMaterial =
            AccessTools.FieldRefAccess<UIDysonEditor, Material>("starMaterial");

        private sealed class Run
        {
            internal StarData Star;
            internal Action OnFlash;
            internal int Tick;
            internal bool Flashed;
            internal float OriginalRadius;

            // The simulator's own colours and light, captured at the start and again after the conversion.
            internal StarSimulator Simulator;
            internal Color BodyCenter;
            internal Color BodyGlow;
            internal Color Halo;
            internal float LightIntensity;
            internal bool HasSimulatorBase;

            // The ejecta shell: a copy of the star's halo glow that expands and fades after the flash.
            internal GameObject Shell;
            internal Material ShellMaterial;
            internal Vector3 ShellBaseScale;

            // The Dyson Sphere editor's star material, captured whenever the editor creates a new one.
            internal Material EditorMaterial;
            internal Color[] EditorColors;
        }

        private static readonly List<Run> Runs = new List<Run>();

        internal static void Begin(StarData star, Action onFlash)
        {
            if (star == null)
            {
                return;
            }

            if (Runs.Exists(run => run.Star == star))
            {
                onFlash?.Invoke();
                return;
            }

            Run started = new Run { Star = star, OnFlash = onFlash, OriginalRadius = star.radius };
            CaptureSimulator(started);
            Runs.Add(started);
        }

        // A save loaded or left mid-animation already records the conversion, so pending flashes are dropped.
        internal static void Reset()
        {
            foreach (Run run in Runs)
            {
                DestroyShell(run);
            }

            Runs.Clear();
        }

        internal static void GameTick()
        {
            for (int i = Runs.Count - 1; i >= 0; i--)
            {
                Run run = Runs[i];
                run.Tick++;
                float time = run.Tick / TicksPerSecond;
                if (!run.Flashed && time >= FlashAt)
                {
                    run.Flashed = true;
                    try
                    {
                        run.OnFlash?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Plugin.ModLogger.LogError("Hyperinjection failed: " + e);
                    }

                    // The conversion recreates the star's colours, so they are captured again.
                    CaptureSimulator(run);
                    CreateShell(run);
                }

                if (time >= End)
                {
                    Finish(run);
                    Runs.RemoveAt(i);
                    continue;
                }

                ApplySimulator(run, time);
            }
        }

        internal static void UpdateEditor(UIDysonEditor editor)
        {
            StarData viewStar = editor?.selection != null ? editor.selection.viewStar : null;
            Run run = viewStar != null ? Runs.Find(r => r.Star == viewStar) : null;
            if (run == null || editor.starRenderer == null)
            {
                return;
            }

            Material material = EditorStarMaterial(editor);
            if (material == null || editor.starMats?.colorPropNames == null)
            {
                return;
            }

            // The editor makes a fresh material when its view star changes, including at the conversion.
            string[] properties = editor.starMats.colorPropNames;
            if (run.EditorMaterial != material)
            {
                run.EditorMaterial = material;
                run.EditorColors = new Color[properties.Length];
                for (int i = 0; i < properties.Length; i++)
                {
                    run.EditorColors[i] = material.HasProperty(properties[i])
                        ? material.GetColor(properties[i])
                        : Color.black;
                }
            }

            float time = run.Tick / TicksPerSecond;
            editor.starRenderer.transform.localScale =
                (float)(viewStar.viewRadius * 2.0 * 0.00025) * GetRadiusFactor(run, time) * Vector3.one;
            for (int i = 0; i < properties.Length && i < run.EditorColors.Length; i++)
            {
                if (material.HasProperty(properties[i]))
                {
                    material.SetColor(properties[i], Shade(run.EditorColors[i], time));
                }
            }
        }

        // Displayed radius over the star's current radius: the original size until the flash, the final size after.
        private static float GetRadiusFactor(Run run, float time)
        {
            float current = Mathf.Max(run.Star.radius, 0.0001f);
            return GetRadius(run, time) / current;
        }

        private static float GetRadius(Run run, float time)
        {
            float original = run.OriginalRadius;
            float final = run.Star.radius;
            if (time < FlashAt)
            {
                // Infall: the surface boils and is pressed in under the arriving mass.
                float ramp = time / FlashAt;
                float boil = 0.03f * ramp * Mathf.Sin(time * 25.1f) * Mathf.Sin(time * 8.2f);
                return original * (1f - 0.06f * Smooth(ramp)) * (1f + boil);
            }

            if (time < ReboundEnd)
            {
                // Core flash: the compressed star rebounds.
                float t = (time - FlashAt) / (ReboundEnd - FlashAt);
                return original * Mathf.Lerp(0.94f, 1.08f, EaseOut(t));
            }

            if (time < SwellEnd)
            {
                // The envelope, out of thermal equilibrium, swells past its new size.
                float t = (time - ReboundEnd) / (SwellEnd - ReboundEnd);
                return Mathf.Lerp(original * 1.08f, final * SwellOvershoot, EaseOut(t));
            }

            // Thermal settling: damped pulses down to the new equilibrium.
            float settle = time - SwellEnd;
            return final * (1f + (SwellOvershoot - 1f) * Mathf.Exp(-0.55f * settle) * Mathf.Cos(settle * 1.96f));
        }

        private static float GetBrightness(float time)
        {
            if (time < FlashAt)
            {
                float ramp = time / FlashAt;
                return 1f + 0.5f * ramp * (0.5f + 0.5f * Mathf.Sin(time * 44f));
            }

            if (time < FlashAt + 0.15f)
            {
                return Mathf.Lerp(1f, PeakBrightness, (time - FlashAt) / 0.15f);
            }

            if (time < SwellEnd)
            {
                return 0.85f + (PeakBrightness - 0.85f) * Mathf.Exp(-(time - FlashAt - 0.15f) * 1.1f);
            }

            return Mathf.Lerp(0.85f, 1f, Smooth((time - SwellEnd) / (End - SwellEnd)));
        }

        // How far the colour moves toward white (the flash) and toward red (the cooling, swollen envelope).
        private static void GetTints(float time, out float white, out float warmth)
        {
            white = time >= FlashAt && time < FlashAt + 0.8f
                ? 0.8f * (1f - (time - FlashAt) / 0.8f)
                : 0f;
            if (time < ReboundEnd)
            {
                warmth = 0f;
            }
            else if (time < SwellEnd)
            {
                warmth = 0.55f * Smooth((time - ReboundEnd) / (SwellEnd - ReboundEnd));
            }
            else
            {
                warmth = 0.55f * (1f - Smooth((time - SwellEnd) / (End - 2f - SwellEnd)));
            }
        }

        private static Color Shade(Color color, float time)
        {
            float white;
            float warmth;
            GetTints(time, out white, out warmth);
            float level = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            Color shaded = Color.Lerp(color, Warm * level, warmth);
            shaded = Color.Lerp(shaded, Color.white * Mathf.Max(level, 1f), white);
            shaded *= Mathf.Sqrt(GetBrightness(time));
            shaded.a = color.a;
            return shaded;
        }

        private static StarSimulator GetSimulator(StarData star)
        {
            StarSimulator[] simulators = GameMain.universeSimulator != null
                ? GameMain.universeSimulator.starSimulators
                : null;
            return simulators != null && star.index < simulators.Length ? simulators[star.index] : null;
        }

        private static void CaptureSimulator(Run run)
        {
            StarSimulator simulator = GetSimulator(run.Star);
            run.Simulator = simulator;
            run.HasSimulatorBase = false;
            Material body = simulator != null ? BodyMaterial(simulator) : null;
            Material halo = simulator != null ? HaloMaterial(simulator) : null;
            if (body == null || halo == null || simulator.sunLight == null)
            {
                return;
            }

            run.BodyCenter = body.GetColor("_SunCenterColor");
            run.BodyGlow = body.GetColor("_SunGlowColor");
            run.Halo = halo.color;
            run.LightIntensity = simulator.sunLight.intensity;
            run.HasSimulatorBase = true;
        }

        // Runs after the game has placed and sized every star for this tick.
        private static void ApplySimulator(Run run, float time)
        {
            StarSimulator simulator = run.Simulator;
            if (!run.HasSimulatorBase || simulator == null || simulator.starData != run.Star)
            {
                return;
            }

            float factor = GetRadiusFactor(run, time);
            float brightness = GetBrightness(time);
            StarData star = run.Star;

            // The game only resizes the body when it is off by more than a unit, so it is set outright here.
            bool giantLook = star.type == EStarType.GiantStar && star.spectr <= ESpectrType.G;
            float body = simulator.visualScale * 6000f * star.radius * (giantLook ? 1.4f : 1f) * factor;
            simulator.bodyRenderer.transform.localScale = new Vector3(body, body, body);

            // The close-up surface layers are set every tick while the player is in the system.
            if (GameMain.localStar == star && simulator.massRenderer.gameObject.activeSelf)
            {
                simulator.massRenderer.transform.localScale *= factor;
                simulator.atmosRenderer.transform.localScale *= factor;
                simulator.effect.transform.localScale *= factor;
                ScaleSunRadius(AtmoMaterial(simulator), factor);
                ScaleSunRadius(EffectMaterial(simulator), factor);
            }

            Material halo = HaloMaterial(simulator);
            halo.SetFloat("_Multiplier", halo.GetFloat("_Multiplier") * brightness);
            halo.color = Shade(run.Halo, time);
            Material bodyMaterial = BodyMaterial(simulator);
            bodyMaterial.SetColor("_SunCenterColor", Shade(run.BodyCenter, time));
            bodyMaterial.SetColor("_SunGlowColor", Shade(run.BodyGlow, time));
            simulator.sunLight.intensity = run.LightIntensity * Mathf.Min(brightness, 3f);
            if (simulator.sunFlare != null)
            {
                simulator.sunFlare.brightness *= Mathf.Min(brightness, 4f);
            }

            UpdateShell(run, time, simulator);
        }

        private static void ScaleSunRadius(Material material, float factor)
        {
            if (material != null && material.HasProperty("_SunPos"))
            {
                Vector4 sun = material.GetVector("_SunPos");
                sun.w *= factor;
                material.SetVector("_SunPos", sun);
            }
        }

        private static void CreateShell(Run run)
        {
            StarSimulator simulator = run.Simulator;
            if (simulator?.haloRenderer == null || HaloMaterial(simulator) == null)
            {
                return;
            }

            run.Shell = UnityEngine.Object.Instantiate(
                simulator.haloRenderer.gameObject,
                simulator.haloRenderer.transform.parent);
            run.Shell.name = "hyperinjection-ejecta";
            Renderer renderer = run.Shell.GetComponent<Renderer>();
            run.ShellMaterial = UnityEngine.Object.Instantiate(HaloMaterial(simulator));
            renderer.sharedMaterial = run.ShellMaterial;
            run.ShellBaseScale = simulator.haloRenderer.transform.localScale;
        }

        // The shell races outward from the flash and fades as it thins.
        private static void UpdateShell(Run run, float time, StarSimulator simulator)
        {
            if (run.Shell == null)
            {
                return;
            }

            float t = Mathf.Clamp01((time - FlashAt) / 7f);
            run.Shell.transform.localScale = run.ShellBaseScale * Mathf.Lerp(1f, 7f, EaseOut(t));
            run.Shell.SetActive(simulator.haloRenderer.enabled && t < 1f);
            Material halo = HaloMaterial(simulator);
            run.ShellMaterial.SetFloat("_Multiplier", halo.GetFloat("_Multiplier") * 0.6f * (1f - t) * (1f - t));
            run.ShellMaterial.color = Color.Lerp(Color.white, Warm, t) * Mathf.Max(1f, run.Halo.maxColorComponent);
        }

        private static void Finish(Run run)
        {
            DestroyShell(run);
            StarSimulator simulator = run.Simulator;
            if (run.HasSimulatorBase && simulator != null && simulator.starData == run.Star)
            {
                BodyMaterial(simulator).SetColor("_SunCenterColor", run.BodyCenter);
                BodyMaterial(simulator).SetColor("_SunGlowColor", run.BodyGlow);
                HaloMaterial(simulator).color = run.Halo;
                simulator.sunLight.intensity = run.LightIntensity;
            }

            if (run.EditorMaterial != null && run.EditorColors != null)
            {
                UIDysonEditor editor = UIRoot.instance?.uiGame?.dysonEditor;
                string[] properties = editor?.starMats?.colorPropNames;
                if (properties != null)
                {
                    for (int i = 0; i < properties.Length && i < run.EditorColors.Length; i++)
                    {
                        if (run.EditorMaterial.HasProperty(properties[i]))
                        {
                            run.EditorMaterial.SetColor(properties[i], run.EditorColors[i]);
                        }
                    }
                }

                if (editor?.starRenderer != null && editor.selection?.viewStar == run.Star)
                {
                    editor.starRenderer.transform.localScale =
                        (float)(run.Star.viewRadius * 2.0 * 0.00025) * Vector3.one;
                }
            }
        }

        private static void DestroyShell(Run run)
        {
            if (run.Shell != null)
            {
                UnityEngine.Object.Destroy(run.Shell);
            }

            if (run.ShellMaterial != null)
            {
                UnityEngine.Object.Destroy(run.ShellMaterial);
            }

            run.Shell = null;
            run.ShellMaterial = null;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float EaseOut(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }
    }

    [HarmonyPatch(typeof(UniverseSimulator), nameof(UniverseSimulator.GameTick))]
    internal static class StarInjectionAnimationTickPatch
    {
        private static void Postfix()
        {
            StarInjectionAnimation.GameTick();
        }
    }

    [HarmonyPatch(typeof(UIDysonEditor), "_OnUpdate")]
    internal static class StarInjectionAnimationEditorPatch
    {
        private static void Postfix(UIDysonEditor __instance)
        {
            StarInjectionAnimation.UpdateEditor(__instance);
        }
    }
}
