using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using Unity.Entities.Runtime.Build;

namespace Unity.TinyConversion
{
    internal static partial class ConversionUtils
    {
        public static Unity.Tiny.Rendering.Fog.Mode ToTiny(this UnityEngine.FogMode fogMode, bool useFog)
        {
            if (!useFog)
                return Unity.Tiny.Rendering.Fog.Mode.None;

            switch (fogMode)
            {
                case UnityEngine.FogMode.Linear:
                    return Unity.Tiny.Rendering.Fog.Mode.Linear;
                case UnityEngine.FogMode.Exponential:
                    return Unity.Tiny.Rendering.Fog.Mode.Exponential;
                case UnityEngine.FogMode.ExponentialSquared:
                    return Unity.Tiny.Rendering.Fog.Mode.ExponentialSquared;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(fogMode), fogMode, null);
            }
        }
    }

    public class RenderSettingsConversion : GameObjectConversionSystem
    {
        public override bool ShouldRunConversionSystem()
        {
            //Workaround for running the tiny conversion systems only if the BuildSettings have the DotsRuntimeBuildProfile component, so these systems won't run in play mode
            if (!TryGetBuildConfigurationComponent<DotsRuntimeBuildProfile>(out _))
                return false;
            return base.ShouldRunConversionSystem();
        }

        protected override void OnUpdate()
        {
            //Get render settings from the current active scene
            Entity e = DstEntityManager.CreateEntity();

            //Ambient light
            DstEntityManager.AddComponentData<Unity.Tiny.Rendering.Light>(e, new Unity.Tiny.Rendering.Light()
            {
                color = new float3(RenderSettings.ambientLight.r, RenderSettings.ambientLight.g, RenderSettings.ambientLight.b),
                intensity = 1.0f
            });
            DstEntityManager.AddComponent<Unity.Tiny.Rendering.AmbientLight>(e);

            //Fog
            var fogLinear = RenderSettings.fogColor.linear;
            DstEntityManager.AddComponentData<Unity.Tiny.Rendering.Fog>(e, new Unity.Tiny.Rendering.Fog()
            {
               mode = RenderSettings.fogMode.ToTiny(RenderSettings.fog),
               color = new float4(fogLinear.r,fogLinear.g, fogLinear.b, fogLinear.a),
               density = RenderSettings.fogDensity,
               startDistance = RenderSettings.fogStartDistance,
               endDistance = RenderSettings.fogEndDistance
            });
        }
    }
}
