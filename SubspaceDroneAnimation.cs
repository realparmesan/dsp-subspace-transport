using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SubspaceTransport
{
    // Shows Subspace Drones on the planet Icarus is on. A departing drone lifts off its restocker, flies a few metres
    // and vanishes into subspace in a violet flash; an arriving one bursts out of subspace beside Icarus and flies in
    // just as its cargo is delivered. The drones are drawn as extra Logistics Bots in the planet's own courier
    // renderer, so they look like the vanilla ones. Purely visual: nothing here is saved.
    internal static class SubspaceDroneAnimation
    {
        internal const int ArrivalTicks = 72;

        private const int DepartureTicks = 72;
        private const float HopDistance = 7f;
        private const float HopRise = 3f;
        private const float ArrivalDistance = 9f;
        private const float ArrivalRise = 4f;

        private static readonly Color SubspaceViolet = new Color(0.7f, 0.35f, 1f, 1f);
        private static readonly Color SubspaceCyan = new Color(0.45f, 0.95f, 1f, 1f);

        private static readonly AccessTools.FieldRef<LogisticCourierRenderer, CourierData[]> CouriersArray =
            AccessTools.FieldRefAccess<LogisticCourierRenderer, CourierData[]>("couriersArr");
        private static readonly AccessTools.FieldRef<LogisticCourierRenderer, ComputeBuffer> CouriersBuffer =
            AccessTools.FieldRefAccess<LogisticCourierRenderer, ComputeBuffer>("couriersBuffer");
        private static readonly AccessTools.FieldRef<LogisticCourierRenderer, int> CourierCount =
            AccessTools.FieldRefAccess<LogisticCourierRenderer, int>("courierCount");
        private static readonly AccessTools.FieldRef<VFEffectEmitter> EmitterInstance =
            AccessTools.StaticFieldRefAccess<VFEffectEmitter>(AccessTools.Field(typeof(VFEffectEmitter), "instance"));

        private sealed class Flight
        {
            internal int PlanetId;
            internal Vector3 From;
            internal Vector3 To;
            internal bool ToPlayer;
            internal int Tick;
            internal int Duration;
            internal int ItemId;
            internal int Count;
            internal int Gene;
        }

        private static readonly List<Flight> Flights = new List<Flight>();
        private static ParticleSystem _burst;

        internal static void Depart(PlanetFactory factory, int entityId, int itemId, int count)
        {
            PlanetData planet = GameMain.localPlanet;
            if (factory?.planet == null || factory.planet != planet || entityId <= 0 || entityId >= factory.entityPool.Length)
            {
                return;
            }

            Vector3 ground = factory.entityPool[entityId].pos;
            Vector3 up = ground.normalized;
            Vector3 from = ground + up * 2f;
            Vector3 to = ground + RandomTangent(up) * HopDistance + up * (2f + HopRise);
            Flights.Add(new Flight
            {
                PlanetId = planet.id,
                From = from,
                To = to,
                Duration = DepartureTicks,
                ItemId = itemId,
                Count = count,
                Gene = Random.Range(0, 1 << 16)
            });
        }

        internal static void Arrive(int itemId, int count)
        {
            PlanetData planet = GameMain.localPlanet;
            Player player = GameMain.mainPlayer;
            if (planet == null || player == null)
            {
                return;
            }

            Vector3 up = player.position.normalized;
            Vector3 from = player.position + RandomTangent(up) * ArrivalDistance + up * ArrivalRise;
            Flights.Add(new Flight
            {
                PlanetId = planet.id,
                From = from,
                To = player.position,
                ToPlayer = true,
                Duration = ArrivalTicks,
                ItemId = itemId,
                Count = count,
                Gene = Random.Range(0, 1 << 16)
            });
            Burst(from);
        }

        internal static void GameTick()
        {
            PlanetData planet = GameMain.localPlanet;
            Player player = GameMain.mainPlayer;
            for (int i = Flights.Count - 1; i >= 0; i--)
            {
                Flight flight = Flights[i];
                if (planet == null || flight.PlanetId != planet.id)
                {
                    Flights.RemoveAt(i);
                    continue;
                }

                if (flight.ToPlayer && player != null)
                {
                    flight.To = player.position;
                }

                if (++flight.Tick < flight.Duration)
                {
                    continue;
                }

                // A departing drone slips into subspace at the end of its hop.
                if (!flight.ToPlayer)
                {
                    Burst(flight.To);
                }

                Flights.RemoveAt(i);
            }
        }

        internal static void Reset()
        {
            Flights.Clear();
        }

        // Appends the animated drones to the courier list the renderer has just uploaded.
        internal static void AppendCouriers(LogisticCourierRenderer renderer)
        {
            PlanetData planet = renderer?.transport?.planet;
            if (planet == null || Flights.Count == 0)
            {
                return;
            }

            int start = CourierCount(renderer);
            int count = start;
            bool expanded = false;
            foreach (Flight flight in Flights)
            {
                if (flight.PlanetId != planet.id)
                {
                    continue;
                }

                if (renderer.capacity <= count)
                {
                    renderer.Expand2x();
                    expanded = true;
                }

                float distance = Mathf.Max(Vector3.Distance(flight.From, flight.To), 0.01f);
                float progress = Mathf.Clamp01(flight.Tick / (float)flight.Duration);
                CouriersArray(renderer)[count++] = new CourierData
                {
                    begin = flight.From,
                    end = flight.To,
                    endId = 0,
                    direction = 1f,
                    maxt = distance,
                    t = distance * Mathf.Min(EaseInOut(progress), 0.99f),
                    itemId = flight.ItemId,
                    itemCount = flight.Count,
                    gene = flight.Gene
                };
            }

            if (count == start)
            {
                return;
            }

            CourierCount(renderer) = count;
            // Growing the renderer replaces its GPU buffer, so the vanilla couriers are uploaded again too.
            int first = expanded ? 0 : start;
            CouriersBuffer(renderer)?.SetData(CouriersArray(renderer), first, first, count - first);
        }

        // A violet flash with a spray of sparks where a drone crosses into or out of subspace.
        private static void Burst(Vector3 planetPosition)
        {
            ParticleSystem burst = GetBurst();
            VFEffectEmitter root = EmitterInstance();
            if (burst == null || root == null)
            {
                return;
            }

            Vector3 position = root.transform.TransformPoint(planetPosition);
            ParticleSystem.EmitParams flash = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = Vector3.zero,
                startColor = Color.Lerp(SubspaceViolet, Color.white, 0.5f),
                startSize = 5f,
                startLifetime = 0.3f,
                applyShapeToPosition = false
            };
            burst.Emit(flash, 1);

            for (int i = 0; i < 36; i++)
            {
                ParticleSystem.EmitParams spark = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = Random.onUnitSphere * Random.Range(4f, 10f),
                    startColor = Color.Lerp(SubspaceViolet, SubspaceCyan, Random.value * 0.6f),
                    startSize = Random.Range(0.25f, 0.6f),
                    startLifetime = Random.Range(0.35f, 0.7f),
                    applyShapeToPosition = false
                };
                burst.Emit(spark, 1);
            }
        }

        // Built from scratch on the material of one of the game's own particle effects, so it renders like them.
        private static ParticleSystem GetBurst()
        {
            if (_burst != null)
            {
                return _burst;
            }

            VFEffectEmitter root = EmitterInstance();
            Material material = null;
            if (LDB.effectEmitters?.dataArray != null)
            {
                foreach (EffectEmitterProto proto in LDB.effectEmitters.dataArray)
                {
                    ParticleSystemRenderer template = proto?.emitter != null
                        ? proto.emitter.GetComponent<ParticleSystemRenderer>()
                        : null;
                    if (template != null && template.sharedMaterial != null)
                    {
                        material = template.sharedMaterial;
                        break;
                    }
                }
            }

            if (root == null || material == null)
            {
                return null;
            }

            GameObject burstObject = new GameObject("subspace-drone-burst");
            burstObject.transform.SetParent(root.transform, false);
            ParticleSystem burst = burstObject.AddComponent<ParticleSystem>();
            burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = burst.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 512;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = burst.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = burst.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = burst.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            ParticleSystem.ColorOverLifetimeModule color = burst.colorOverLifetime;
            color.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;

            ParticleSystem.LimitVelocityOverLifetimeModule drag = burst.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.15f;
            drag.limit = 0f;

            ParticleSystemRenderer renderer = burstObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            burst.Play();
            _burst = burst;
            return burst;
        }

        private static Vector3 RandomTangent(Vector3 up)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(Random.onUnitSphere, up);
            return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.Cross(up, Vector3.right).normalized;
        }

        private static float EaseInOut(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }

    [HarmonyPatch(typeof(LogisticCourierRenderer), nameof(LogisticCourierRenderer.Update))]
    internal static class SubspaceDroneCourierPatch
    {
        private static void Postfix(LogisticCourierRenderer __instance)
        {
            SubspaceDroneAnimation.AppendCouriers(__instance);
        }
    }
}
