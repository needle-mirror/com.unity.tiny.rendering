using System;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Tiny.Rendering;
using Unity.Transforms;
using Unity.Entities.Runtime.Build;
using Unity.Tiny.Particles;

namespace Unity.TinyConversion
{
    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))]
    class ParticleSystemDeclareAssets : GameObjectConversionSystem
    {
        public override bool ShouldRunConversionSystem()
        {
            // Workaround for running the tiny conversion systems only if the BuildSettings have the DotsRuntimeBuildProfile component, so these systems won't run in play mode
            if (!TryGetBuildConfigurationComponent<DotsRuntimeBuildProfile>(out _))
                return false;
            return base.ShouldRunConversionSystem();
        }

        protected override void OnUpdate() =>
            Entities.ForEach((UnityEngine.ParticleSystemRenderer uParticleSystemRenderer) =>
            {
                DeclareReferencedAsset(uParticleSystemRenderer.sharedMaterial);

                if (uParticleSystemRenderer.renderMode == ParticleSystemRenderMode.Mesh)
                {
                    if (uParticleSystemRenderer.mesh == null)
                        UnityEngine.Debug.LogWarning("Missing mesh in ParticleSystemRenderer on gameobject: " + uParticleSystemRenderer.gameObject.name);

                    DeclareReferencedAsset(uParticleSystemRenderer.mesh);
                }
            });
    }

    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    [UpdateAfter(typeof(MaterialConversion))]
    [UpdateAfter(typeof(MeshConversion))]
    public class ParticleSystemConversion : GameObjectConversionSystem
    {
        public override bool ShouldRunConversionSystem()
        {
            // Workaround for running the tiny conversion systems only if the BuildSettings have the DotsRuntimeBuildProfile component, so these systems won't run in play mode
            if (!TryGetBuildConfigurationComponent<DotsRuntimeBuildProfile>(out _))
                return false;
            return base.ShouldRunConversionSystem();
        }

        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.ParticleSystem uParticleSystem) =>
            {
                var eParticleSystem = GetPrimaryEntity(uParticleSystem);
                AddTransforms(ref uParticleSystem, eParticleSystem);

                Entity eParticle = CreateParticleTemplate(ref uParticleSystem);

                if (uParticleSystem.emission.enabled)
                {
                    DstEntityManager.AddComponentData<ParticleEmitter>(eParticleSystem, new ParticleEmitter
                    {
                        particle = eParticle,
                        maxParticles = (uint)uParticleSystem.main.maxParticles,
                        emitRate = ConvertMinMaxCurve(uParticleSystem.emission.rateOverTime),
                        lifetime = ConvertMinMaxCurve(uParticleSystem.main.startLifetime),
                        attachToEmitter = uParticleSystem.main.simulationSpace == ParticleSystemSimulationSpace.Local
                    });

                    if (uParticleSystem.emission.burstCount > 0)
                    {
                        UnityEngine.ParticleSystem.Burst[] bursts = new UnityEngine.ParticleSystem.Burst[uParticleSystem.emission.burstCount];
                        uParticleSystem.emission.GetBursts(bursts);
                        // TODO support multiple bursts with IBufferElementData or by creating a new entity for each burst emitter
                        //foreach (var burst in bursts)
                        var burst = bursts[0];
                        {
                            DstEntityManager.AddComponentData<BurstEmission>(eParticleSystem, new BurstEmission
                            {
                                count = ConvertMinMaxCurve(burst.count),
                                interval = burst.repeatInterval,
                                cycles = burst.cycleCount
                                // TODO probability
                                // TODO time
                            });
                        }
                    }
                }

                AddEmitterSource(ref uParticleSystem, eParticleSystem);

                ParticleSystemRenderer uParticleSystemRenderer = uParticleSystem.gameObject.GetComponent<ParticleSystemRenderer>();
                if (uParticleSystemRenderer.renderMode == ParticleSystemRenderMode.Billboard)
                    DstEntityManager.AddComponentData<Billboarded>(eParticleSystem, new Billboarded());
            });
        }

        private void AddTransforms(ref UnityEngine.ParticleSystem uParticleSystem, Entity eParticleSystem)
        {
            // TODO further investigate custom transform expected behavior
            /*if (uParticleSystem.main.simulationSpace == ParticleSystemSimulationSpace.Custom)
            {
                DstEntityManager.AddComponentData(eParticleSystem, new LocalToWorld { Value = uParticleSystem.main.customSimulationSpace.localToWorldMatrix });
                DstEntityManager.AddComponentData(eParticleSystem, new Translation { Value = uParticleSystem.main.customSimulationSpace.position });
                DstEntityManager.AddComponentData(eParticleSystem, new Rotation { Value = uParticleSystem.main.customSimulationSpace.rotation });
                if (uParticleSystem.main.customSimulationSpace.lossyScale != Vector3.one)
                    DstEntityManager.AddComponentData(eParticleSystem, new NonUniformScale { Value = uParticleSystem.main.customSimulationSpace.lossyScale });
            }*/

            // Emitter initial rotation
            if (uParticleSystem.main.startRotation3D)
            {
                DstEntityManager.AddComponentData<EmitterInitialNonUniformRotation>(eParticleSystem, new EmitterInitialNonUniformRotation
                {
                    angleX = ConvertMinMaxCurve(uParticleSystem.main.startRotationX),
                    angleY = ConvertMinMaxCurve(uParticleSystem.main.startRotationY),
                    angleZ = ConvertMinMaxCurve(uParticleSystem.main.startRotationZ)
                });
            }
            else
            {
                DstEntityManager.AddComponentData<EmitterInitialRotation>(eParticleSystem, new EmitterInitialRotation
                {
                    angle = ConvertMinMaxCurve(uParticleSystem.main.startRotation)
                });
            }

            // Emitter initial scale
            if (uParticleSystem.main.startSize3D)
            {
                DstEntityManager.AddComponentData<EmitterInitialNonUniformScale>(eParticleSystem, new EmitterInitialNonUniformScale
                {
                    scaleX = ConvertMinMaxCurve(uParticleSystem.main.startSizeX),
                    scaleY = ConvertMinMaxCurve(uParticleSystem.main.startSizeY),
                    scaleZ = ConvertMinMaxCurve(uParticleSystem.main.startSizeZ)
                });
            }
            else
            {
                DstEntityManager.AddComponentData<EmitterInitialScale>(eParticleSystem, new EmitterInitialScale
                {
                    scale = ConvertMinMaxCurve(uParticleSystem.main.startSize)
                });
            }
        }

        private Entity CreateParticleTemplate(ref UnityEngine.ParticleSystem uParticleSystem)
        {
            Entity eParticle = DstEntityManager.CreateEntity();

            // Mesh Renderer
            ParticleSystemRenderer uParticleSystemRenderer = uParticleSystem.gameObject.GetComponent<ParticleSystemRenderer>();
            var eMaterial = GetPrimaryEntity(uParticleSystemRenderer.sharedMaterial);
            Unity.Tiny.Rendering.MeshRenderer meshRenderer = new Unity.Tiny.Rendering.MeshRenderer { material = eMaterial };
            if (uParticleSystemRenderer.renderMode == ParticleSystemRenderMode.Mesh)
            {
                meshRenderer.mesh = GetPrimaryEntity(uParticleSystemRenderer.mesh);
                var lmrd = DstEntityManager.GetComponentData<LitMeshRenderData>(meshRenderer.mesh);
                meshRenderer.startIndex = 0;
                meshRenderer.indexCount = lmrd.Mesh.Value.Indices.Length;
            }
            DstEntityManager.AddComponentData(eParticle, meshRenderer);
            DstEntityManager.AddComponentData(eParticle, new Unity.Tiny.Rendering.LitMeshRenderer());

            // Transform
            DstEntityManager.AddComponentData<LocalToWorld>(eParticle, new LocalToWorld
            {
                Value = float4x4.identity
            });

            DstEntityManager.AddComponentData<WorldBounds>(eParticle, new WorldBounds());

            return eParticle;
        }

        private void AddEmitterSource(ref UnityEngine.ParticleSystem uParticleSystem, Entity eParticleSystem)
        {
            if (!uParticleSystem.shape.enabled)
                return;

            switch (uParticleSystem.shape.shapeType)
            {
                case ParticleSystemShapeType.Cone:
                    DstEntityManager.AddComponentData<EmitterConeSource>(eParticleSystem, new EmitterConeSource
                    {
                        radius = uParticleSystem.shape.radius,
                        speed = ConvertMinMaxCurve(uParticleSystem.main.startSpeed),
                        angle = uParticleSystem.shape.angle
                    });
                    break;
                case ParticleSystemShapeType.Circle:
                    DstEntityManager.AddComponentData<EmitterCircleSource>(eParticleSystem, new EmitterCircleSource
                    {
                        speed = ConvertMinMaxCurve(uParticleSystem.main.startSpeed),
                        radius = uParticleSystem.shape.radius
                    });
                    break;
                case ParticleSystemShapeType.Rectangle:
                    DstEntityManager.AddComponentData<EmitterRectangleSource>(eParticleSystem, new EmitterRectangleSource
                    {
                        rect = Unity.Tiny.Rect.Default,
                        speed = ConvertMinMaxCurve(uParticleSystem.main.startSpeed)
                    });
                    break;
                default:
                    UnityEngine.Debug.LogWarning("ParticleSystemShapeType " + nameof(uParticleSystem.shape.shapeType) + " not supported.");
                    break;
            }
        }
        // TODO support curves
        private Range ConvertMinMaxCurve(UnityEngine.ParticleSystem.MinMaxCurve curve)
        {
            switch(curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new Range { start = curve.constant, end = curve.constant };
                case ParticleSystemCurveMode.TwoConstants:
                    return new Range { start = curve.constantMin, end = curve.constantMax };
                case ParticleSystemCurveMode.Curve:
                case ParticleSystemCurveMode.TwoCurves:
                    UnityEngine.Debug.LogWarning("ParticleSystemCurveMode " + nameof(curve.mode) + " not supported.");
                    return new Range();
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(curve.mode), curve.mode, null);
            }
        }
    }

    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    [UpdateBefore(typeof(MeshConversion))]
    public class ParticleSystemRendererConversion : GameObjectConversionSystem
    {
        public override bool ShouldRunConversionSystem()
        {
            // Workaround for running the tiny conversion systems only if the BuildSettings have the DotsRuntimeBuildProfile component, so these systems won't run in play mode
            if (!TryGetBuildConfigurationComponent<DotsRuntimeBuildProfile>(out _))
                return false;
            return base.ShouldRunConversionSystem();
        }

        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.ParticleSystemRenderer uParticleSystemRenderer) =>
            {
                var eParticleSystemRenderer = GetPrimaryEntity(uParticleSystemRenderer);
                UnityEngine.Mesh uMesh = uParticleSystemRenderer.mesh;
                if (uParticleSystemRenderer.renderMode == ParticleSystemRenderMode.Mesh && uMesh != null)
                {
                    var meshEntity = GetPrimaryEntity(uMesh);
                    DstEntityManager.AddComponent<LitMeshRenderData>(meshEntity);
                }
            });
        }
    }
}
