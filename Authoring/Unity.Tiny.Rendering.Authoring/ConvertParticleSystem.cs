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
    [WorldSystemFilter(WorldSystemFilterFlags.DotsRuntimeGameObjectConversion)]
    class ParticleSystemDeclareAssets : GameObjectConversionSystem
    {
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
    [WorldSystemFilter(WorldSystemFilterFlags.DotsRuntimeGameObjectConversion)]
    public class ParticleSystemConversion : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.ParticleSystem uParticleSystem) =>
            {
                var eParticleSystem = GetPrimaryEntity(uParticleSystem);
                AddTransforms(ref uParticleSystem, eParticleSystem);
                Entity eParticle = DstEntityManager.CreateEntity();
                DstEntityManager.AddComponentData<LocalToWorld>(eParticle, new LocalToWorld { Value = float4x4.identity });
                DstEntityManager.AddComponentData<WorldBounds>(eParticle, new WorldBounds());

                // General settings
                ParticleEmitter particleEmitter = new ParticleEmitter
                {
                    particle = eParticle,
                    duration = uParticleSystem.main.duration,
                    maxParticles = (uint)uParticleSystem.main.maxParticles,
                    lifetime = ConvertMinMaxCurve(uParticleSystem.main.startLifetime),
                    attachToEmitter = uParticleSystem.main.simulationSpace == ParticleSystemSimulationSpace.Local,
                };

                DstEntityManager.AddComponentData(eParticleSystem, new EmitterInitialSpeed { speed = ConvertMinMaxCurve(uParticleSystem.main.startSpeed) });

                if (uParticleSystem.main.loop)
                    DstEntityManager.AddComponentData(eParticleSystem, new Looping());

                DstEntityManager.AddComponentData(eParticleSystem, new StartDelay { delay = ConvertMinMaxCurve(uParticleSystem.main.startDelay) });
                DstEntityManager.AddComponentData(eParticleSystem, ConvertMinMaxGradient(uParticleSystem.main.startColor));

                if (!uParticleSystem.useAutoRandomSeed)
                    DstEntityManager.AddComponentData(eParticleSystem, new RandomSeed { seed = uParticleSystem.randomSeed });

                // Emission settings
                if (uParticleSystem.emission.enabled)
                {
                    particleEmitter.emitRate = ConvertMinMaxCurve(uParticleSystem.emission.rateOverTime);

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

                DstEntityManager.AddComponentData<ParticleEmitter>(eParticleSystem, particleEmitter);

                // Shape settings
                AddEmitterSource(ref uParticleSystem, eParticleSystem);
                DstEntityManager.AddComponentData(eParticleSystem, new RandomizeDirection { Value = uParticleSystem.shape.randomDirectionAmount });
                DstEntityManager.AddComponentData(eParticleSystem, new RandomizePosition { Value = uParticleSystem.shape.randomPositionAmount });

                // Renderer settings
                ParticleSystemRenderer uParticleSystemRenderer = uParticleSystem.gameObject.GetComponent<ParticleSystemRenderer>();
                DstEntityManager.AddComponentData(eParticleSystem, new ParticleMaterial { material = GetPrimaryEntity(uParticleSystemRenderer.sharedMaterial) });

                if (uParticleSystemRenderer.renderMode == ParticleSystemRenderMode.Billboard)
                    DstEntityManager.AddComponentData<Billboarded>(eParticleSystem, new Billboarded());
                else if (uParticleSystemRenderer.renderMode == ParticleSystemRenderMode.Mesh)
                    DstEntityManager.AddComponentData(eParticleSystem, new ParticleMesh { mesh = GetPrimaryEntity(uParticleSystemRenderer.mesh) });
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

        private void AddEmitterSource(ref UnityEngine.ParticleSystem uParticleSystem, Entity eParticleSystem)
        {
            if (!uParticleSystem.shape.enabled)
                return;

            switch (uParticleSystem.shape.shapeType)
            {
                case ParticleSystemShapeType.Cone:
                    DstEntityManager.AddComponentData(eParticleSystem, new EmitterConeSource
                    {
                        radius = uParticleSystem.shape.radius,
                        angle = uParticleSystem.shape.angle
                    });
                    break;
                case ParticleSystemShapeType.Circle:
                    DstEntityManager.AddComponentData(eParticleSystem, new EmitterCircleSource
                    {
                        radius = uParticleSystem.shape.radius
                    });
                    break;
                case ParticleSystemShapeType.Rectangle:
                    DstEntityManager.AddComponentData(eParticleSystem, new EmitterRectangleSource());
                    break;
                case ParticleSystemShapeType.Sphere:
                    DstEntityManager.AddComponentData(eParticleSystem, new EmitterSphereSource
                    {
                        radius = uParticleSystem.shape.radius
                    });
                    break;
                case ParticleSystemShapeType.Hemisphere:
                    DstEntityManager.AddComponentData(eParticleSystem, new EmitterHemisphereSource
                    {
                        radius = uParticleSystem.shape.radius
                    });
                    break;
                default:
                    UnityEngine.Debug.LogWarning("ParticleSystemShapeType " + nameof(uParticleSystem.shape.shapeType) + " not supported.");
                    break;
            }
        }

        // TODO support curves
        private static Range ConvertMinMaxCurve(UnityEngine.ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
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

        // TODO support gradients
        private static InitialColor ConvertMinMaxGradient(UnityEngine.ParticleSystem.MinMaxGradient gradient)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                {
                    float4 color = new float4(gradient.color.r, gradient.color.g, gradient.color.b, gradient.color.a);
                    return new InitialColor { colorMin = color, colorMax = color };
                }
                case ParticleSystemGradientMode.TwoColors:
                    return new InitialColor
                    {
                        colorMin = new float4(gradient.colorMin.r, gradient.colorMin.g, gradient.colorMin.b, gradient.colorMin.a),
                        colorMax = new float4(gradient.colorMax.r, gradient.colorMax.g, gradient.colorMax.b, gradient.colorMax.a)
                    };
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.TwoGradients:
                case ParticleSystemGradientMode.RandomColor:
                {
                    UnityEngine.Debug.LogWarning("ParticleSystemGradientMode " + nameof(gradient.mode) + " not supported.");
                    float4 defaultColor = new float4(1);
                    return new InitialColor { colorMin = defaultColor, colorMax = defaultColor };
                }

                default:
                    throw new System.ArgumentOutOfRangeException(nameof(gradient.mode), gradient.mode, null);
            }
        }
    }

    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    [UpdateBefore(typeof(MeshConversion))]
    [UpdateAfter(typeof(MaterialConversion))]
    [WorldSystemFilter(WorldSystemFilterFlags.DotsRuntimeGameObjectConversion)]
    public class ParticleSystemRendererConversion : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.ParticleSystemRenderer uParticleSystemRenderer) =>
            {
                var eParticleSystemRenderer = GetPrimaryEntity(uParticleSystemRenderer);
                UnityEngine.Mesh uMesh = uParticleSystemRenderer.mesh;
                if (uParticleSystemRenderer.renderMode == ParticleSystemRenderMode.Mesh && uMesh != null)
                {
                    var eMesh = GetPrimaryEntity(uMesh);
                    var eMaterial = GetPrimaryEntity(uParticleSystemRenderer.sharedMaterial);
                    if (DstEntityManager.HasComponent<LitMaterial>(eMaterial))
                    {
                        DstEntityManager.AddComponent<LitMeshRenderData>(eMesh);
                        DstEntityManager.RemoveComponent<SimpleMeshRenderData>(eMesh);
                    }
                    else if (DstEntityManager.HasComponent<SimpleMaterial>(eMaterial))
                    {
                        DstEntityManager.AddComponent<SimpleMeshRenderData>(eMesh);
                        DstEntityManager.RemoveComponent<LitMeshRenderData>(eMesh);
                    }
                }
            });
        }
    }
}
