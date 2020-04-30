using System;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Tiny.Assertions;
using Unity.Transforms;
using Bgfx;

namespace Unity.Tiny.Rendering
{
    public struct MappedLightBGFX
    {
        public bgfx.TextureHandle shadowMap;
        public float4x4 projection;
        public float4 color_invrangesqr;
        public float4 worldPosOrDir;
        public float4 mask;

        public void Set(float4x4 m, float3 color, float4 _worldPosOrDir, float _range, float4 _mask, bgfx.TextureHandle _shadowMap)
        {
            projection = m;
            color_invrangesqr = new float4(color, LightingBGFX.InverseSquare(_range) * _worldPosOrDir.w);
            worldPosOrDir = _worldPosOrDir;
            mask = _mask;
            shadowMap = _shadowMap;
        }
    }

    public unsafe struct LightingViewSpaceBGFX
    {
        public fixed float podl_positionOrDirViewSpace[LightingBGFX.maxPointOrDirLights * 4];
        public float4 mappedLight0_viewPosOrDir;
        public float4 mappedLight1_viewPosOrDir;
        public float4 csmLight_viewPosOrDir;
        public int cacheTag; // must init and invalidate as -1
    }

    public unsafe struct LightingBGFX : IComponentData
    {
        static public float InverseSquare(float x)
        {
            if (x <= 0.0f)
                return 0.0f;
            x = 1.0f / x;
            return x * x;
        }

        public const int maxMappedLights = 2;
        public int numMappedLights;
        public MappedLightBGFX mappedLight0;
        public MappedLightBGFX mappedLight1;
        public float4 mappedLight01sis;

        public const int maxCsmLights = 1;
        public int numCsmLights;
        public MappedLightBGFX csmLight; // also mapped light2
        public float4 csmLightsis;
        public fixed float csmOffsetScale[4 * 4];

        public void SetMappedLight(int idx, float4x4 m, float3 color, float4 worldPosOrDir, float range, float4 mask, bgfx.TextureHandle shadowMap, int shadowMapSize)
        {
            switch (idx)
            {
                case 0:
                    mappedLight0.Set(m, color, worldPosOrDir, range, mask, shadowMap);
                    mappedLight01sis.x = (float)shadowMapSize;
                    mappedLight01sis.y = 1.0f / (float)shadowMapSize;
                    break;
                case 1:
                    mappedLight1.Set(m, color, worldPosOrDir, range, mask, shadowMap);
                    mappedLight01sis.z = (float)shadowMapSize;
                    mappedLight01sis.w = 1.0f / (float)shadowMapSize;
                    break;
                case 2:
                    csmLight.Set(m, color, worldPosOrDir, range, mask, shadowMap);
                    csmLightsis.x = (float)shadowMapSize; // full size, not cascade size
                    csmLightsis.y = 1.0f / (float)shadowMapSize;
                    csmLightsis.z = 1.0f - 3.0f * csmLightsis.y; // border around cascades, in normalized [-1..1]
                    break;
                default: throw new IndexOutOfRangeException();
            }
            ;
        }

        public const int maxPointOrDirLights = 8;
        public int numPointOrDirLights;
        public fixed float podl_positionOrDir[maxPointOrDirLights * 4];
        public fixed float podl_colorIVR[maxPointOrDirLights * 4];

        public void TransformToViewSpace(ref float4x4 viewTx, ref LightingViewSpaceBGFX dest, ushort viewId)
        {
            if (dest.cacheTag == viewId)
                return;
            // simple lights
            fixed(float* pDest = dest.podl_positionOrDirViewSpace, pSrc = podl_positionOrDir)
            {
                for (int i = 0; i < numPointOrDirLights; i++)
                    *(float4*)(pDest + (i << 2)) = math.mul(viewTx, *(float4*)(pSrc + (i << 2)));
            }
            // mapped lights
            dest.mappedLight0_viewPosOrDir = math.mul(viewTx, mappedLight0.worldPosOrDir);
            dest.mappedLight1_viewPosOrDir = math.mul(viewTx, mappedLight1.worldPosOrDir);
            dest.csmLight_viewPosOrDir = math.mul(viewTx, csmLight.worldPosOrDir);
            dest.cacheTag = viewId;
        }

        public void SetPointLight(int idx, float3 pos, float range, float3 color)
        {
            Assert.IsTrue(idx >= 0 && idx < maxPointOrDirLights);
            Assert.IsTrue(math.lengthsq(color) > 0.0f);
            Assert.IsTrue(range > 0.0f);
            idx <<= 2;
            podl_positionOrDir[idx] = pos.x;
            podl_positionOrDir[idx + 1] = pos.y;
            podl_positionOrDir[idx + 2] = pos.z;
            podl_positionOrDir[idx + 3] = 1.0f;
            podl_colorIVR[idx] = color.x;
            podl_colorIVR[idx + 1] = color.y;
            podl_colorIVR[idx + 2] = color.z;
            podl_colorIVR[idx + 3] = InverseSquare(range);
        }

        public void SetDirLight(int idx, float3 dirWorldSpace, float3 color)
        {
            Assert.IsTrue(idx >= 0 && idx < maxPointOrDirLights);
            idx <<= 2;
            Assert.IsTrue(math.lengthsq(color) > 0.0f);
            Assert.IsTrue(math.lengthsq(dirWorldSpace) > 0.0f);
            podl_positionOrDir[idx] = -dirWorldSpace.x;
            podl_positionOrDir[idx + 1] = -dirWorldSpace.y;
            podl_positionOrDir[idx + 2] = -dirWorldSpace.z;
            podl_positionOrDir[idx + 3] = 0.0f;
            podl_colorIVR[idx] = color.x;
            podl_colorIVR[idx + 1] = color.y;
            podl_colorIVR[idx + 2] = color.z;
            podl_colorIVR[idx + 3] = 0.0f;
        }

        public float4 ambient;

        // fogParams.x - Fog mode. Stored as flags where 0 = None, 1 = Linear, 2 = Exp, 4 = Exp2
        // fogParams.y - Fog density. Used for exponential and exponential squared fog
        // fogParams.z - Distance from camera at which fog completely obscures scene object. Used for linear fog
        // fogParams.w - Constant for 1 / (end - start), where 'start' is the distance from camera at which fog starts, and 'end' is equal to fogParams.z. Used for linear fog
        public float4 fogParams;
        public float4 fogColor;
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(UpdateLightMatricesSystem))]
    [UpdateAfter(typeof(RendererBGFXSystem))]
    [UpdateBefore(typeof(SubmitSystemGroup))]
    public unsafe class UpdateBGFXLightSetups : SystemBase
    {
        private void AddCascadeMappedLight(ref LightingBGFX r, ref ShadowmappedLight sml, ref Light l, ref float4x4 tx, ref LightMatrices txCache,
            ref CascadeShadowmappedLight csm, ref CascadeShadowmappedLightCache csmData, RendererBGFXInstance *sys, bool srgbColors)
        {
            if (r.numCsmLights >= LightingBGFX.maxCsmLights)
                throw new InvalidOperationException("Too many cascade mapped lights");
            bgfx.TextureHandle texShadowMap = sys->m_noShadow;
            int shadowMapSize = 1;
            if (sml.shadowMap != Entity.Null && EntityManager.HasComponent<TextureBGFX>(sml.shadowMap))
            {
                var imShadowMap = EntityManager.GetComponentData<Image2D>(sml.shadowMap);
                Assert.IsTrue(imShadowMap.imagePixelWidth == imShadowMap.imagePixelHeight);
                shadowMapSize = imShadowMap.imagePixelHeight;
                texShadowMap = EntityManager.GetComponentData<TextureBGFX>(sml.shadowMap).handle;
            }
            float4 worldPosOrDir = new float4(math.normalize(-tx.c2.xyz), 0.0f);
            float4 mask = new float4(0);
            float3 c = srgbColors ? Color.LinearToSRGB(l.color) : l.color;
            r.SetMappedLight(LightingBGFX.maxMappedLights + r.numCsmLights, txCache.mvp, c * l.intensity, worldPosOrDir, l.clipZFar, mask, texShadowMap, shadowMapSize);
            unsafe {
                r.csmOffsetScale[0]  = csmData.c0.offset.x; r.csmOffsetScale[1]  = csmData.c0.offset.y; r.csmOffsetScale[2]  = 0; r.csmOffsetScale[3]  = csmData.c0.scale;
                r.csmOffsetScale[4]  = csmData.c1.offset.x; r.csmOffsetScale[5]  = csmData.c1.offset.y; r.csmOffsetScale[6]  = 0; r.csmOffsetScale[7]  = csmData.c1.scale;
                r.csmOffsetScale[8]  = csmData.c2.offset.x; r.csmOffsetScale[9]  = csmData.c2.offset.y; r.csmOffsetScale[10] = 0; r.csmOffsetScale[11] = csmData.c2.scale;
                r.csmOffsetScale[12] = csmData.c3.offset.x; r.csmOffsetScale[13] = csmData.c3.offset.y; r.csmOffsetScale[14] = 0; r.csmOffsetScale[15] = csmData.c3.scale;
            }
            // r.csmLightsis set in SetMappedLight
            r.numCsmLights++;
        }

        private void AddMappedLight(ref LightingBGFX r, ref ShadowmappedLight sml, ref Light l, ref float4x4 tx, ref LightMatrices txCache, RendererBGFXInstance *sys, bool isSpot, float4 spotmask, bool srgbColors)
        {
            if (r.numMappedLights >= LightingBGFX.maxMappedLights)
                throw new InvalidOperationException("Too many mapped lights");
            bgfx.TextureHandle texShadowMap = sys->m_noShadow;
            int shadowMapSize = 1;
            if (sml.shadowMap != Entity.Null && EntityManager.HasComponent<TextureBGFX>(sml.shadowMap))
            {
                var imShadowMap = EntityManager.GetComponentData<Image2D>(sml.shadowMap);
                Assert.IsTrue(imShadowMap.imagePixelWidth == imShadowMap.imagePixelHeight);
                shadowMapSize = imShadowMap.imagePixelHeight;
                texShadowMap = EntityManager.GetComponentData<TextureBGFX>(sml.shadowMap).handle;
            }
            float4 mask = isSpot ? spotmask : new float4(0.0f, 0.0f, 0.0f, 1.0f);
            float4 worldPosOrDir = isSpot ? new float4(tx.c3.xyz, 1.0f) : new float4(-tx.c2.xyz, 0.0f);
            float3 c = srgbColors ? Color.LinearToSRGB(l.color) : l.color;
            r.SetMappedLight(r.numMappedLights, txCache.mvp, c * l.intensity, worldPosOrDir, l.clipZFar, mask, texShadowMap, shadowMapSize);
            r.numMappedLights++;
        }

        private float4 ComputeSpotMask(float innerRadius, float ratio)
        {
            Assert.IsTrue(innerRadius >= 0.0f && innerRadius < 1.0f);
            Assert.IsTrue(ratio > 0.0f && ratio <= 1.0f);
            // math in shader:
            // vec2 s = params.xy * ndcpos.xy;
            // return min ( max ( params.z - dot(s, s), params.w ), 1.0 );
            // unit: innerRadius = 0, ratio = 1
            float iri = 1.0f / (1.0f - innerRadius);
            float siri = math.sqrt(iri);
            return new float4(siri, 1.0f / ratio * siri, 1.0f + innerRadius * iri, 0.0f);
        }

        protected override void OnUpdate()
        {
            var sys = World.GetExistingSystem<RendererBGFXSystem>().InstancePointer();
            Dependency.Complete();

            var di = GetSingleton<DisplayInfo>();
            bool srgbColors = di.colorSpace == ColorSpace.Gamma;

            // reset lighting
            Entities.ForEach((ref LightingBGFX r) =>
            {
                r = default;
                r.mappedLight0.shadowMap = sys->m_noShadow;
                r.mappedLight1.shadowMap = sys->m_noShadow;
                r.csmLight.shadowMap = sys->m_noShadow;
            }).Run();

            Entities.WithoutBurst().ForEach((DynamicBuffer<LightToLightingSetup> dest, ref Light l, ref AmbientLight al) =>
            {
                for (int i = 0; i < dest.Length; i++)
                {
                    LightingBGFX r = EntityManager.GetComponentData<LightingBGFX>(dest[i].e);
                    float3 c = srgbColors ? Color.LinearToSRGB(l.color) : l.color;
                    r.ambient.xyz += c * l.intensity;
                    EntityManager.SetComponentData<LightingBGFX>(dest[i].e, r);
                }
            }).Run();

            Entities.WithoutBurst().WithNone<ShadowmappedLight, AmbientLight>().ForEach((Entity e, DynamicBuffer<LightToLightingSetup> dest, ref LocalToWorld tx, ref Light l, ref DirectionalLight dl) =>
            {
                for (int i = 0; i < dest.Length; i++)
                {
                    LightingBGFX r = EntityManager.GetComponentData<LightingBGFX>(dest[i].e);
                    if (r.numPointOrDirLights >= LightingBGFX.maxPointOrDirLights)
                        throw new InvalidOperationException("Too many directional lights");
                    float3 c = srgbColors ? Color.LinearToSRGB(l.color) : l.color;
                    r.SetDirLight(r.numPointOrDirLights, math.normalize(tx.Value.c2.xyz), c * l.intensity);
                    r.numPointOrDirLights++;
                    EntityManager.SetComponentData<LightingBGFX>(dest[i].e, r);
                }
            }).Run();

            Entities.WithoutBurst().WithNone<ShadowmappedLight, DirectionalLight, AmbientLight>().ForEach((Entity e, DynamicBuffer<LightToLightingSetup> dest, ref LocalToWorld tx, ref Light l) =>
            {
                for (int i = 0; i < dest.Length; i++)
                {
                    LightingBGFX r = EntityManager.GetComponentData<LightingBGFX>(dest[i].e);
                    if (r.numPointOrDirLights >= LightingBGFX.maxPointOrDirLights)
                        throw new InvalidOperationException("Too many point lights");
                    float3 c = srgbColors ? Color.LinearToSRGB(l.color) : l.color;
                    r.SetPointLight(r.numPointOrDirLights, tx.Value.c3.xyz, l.clipZFar, c * l.intensity);
                    r.numPointOrDirLights++;
                    EntityManager.SetComponentData<LightingBGFX>(dest[i].e, r);
                }
            }).Run();

            Entities.WithoutBurst().ForEach((Entity e, DynamicBuffer<LightToLightingSetup> dest, ref LocalToWorld tx, ref LightMatrices txCache, ref Light l, ref SpotLight sl, ref ShadowmappedLight sml) =>
            {
                // matrix for now: world -> light projection
                for (int i = 0; i < dest.Length; i++)
                {
                    LightingBGFX r = EntityManager.GetComponentData<LightingBGFX>(dest[i].e);
                    float4 spotmask = ComputeSpotMask(sl.innerRadius, sl.ratio);
                    AddMappedLight(ref r, ref sml, ref l, ref tx.Value, ref txCache, sys, true, spotmask, srgbColors);
                    EntityManager.SetComponentData<LightingBGFX>(dest[i].e, r);
                }
            }).Run();

            Entities.WithoutBurst().ForEach((Entity e, DynamicBuffer<LightToLightingSetup> dest, ref LocalToWorld tx,
                ref LightMatrices txCache, ref Light l, ref DirectionalLight dl, ref ShadowmappedLight sml) =>
                {
                    // matrix: world -> light projection
                    Assert.IsTrue(EntityManager.HasComponent<NonUniformScale>(e));
                    for (int i = 0; i < dest.Length; i++)
                    {
                        LightingBGFX r = EntityManager.GetComponentData<LightingBGFX>(dest[i].e);
                        if (EntityManager.HasComponent<CascadeShadowmappedLight>(e))
                        {
                            var csm = EntityManager.GetComponentData<CascadeShadowmappedLight>(e);
                            var csmData = EntityManager.GetComponentData<CascadeShadowmappedLightCache>(e);
                            AddCascadeMappedLight(ref r, ref sml, ref l, ref tx.Value, ref txCache, ref csm, ref csmData, sys, srgbColors);
                        }
                        else
                        {
                            AddMappedLight(ref r, ref sml, ref l, ref tx.Value, ref txCache, sys, false, new float4(0), srgbColors);
                        }
                        EntityManager.SetComponentData<LightingBGFX>(dest[i].e, r);
                    }
                }).Run();

            Entities.WithoutBurst().ForEach((Entity e, DynamicBuffer<LightToLightingSetup> dest, ref Light l, ref Fog fog) =>
            {
                for (int i = 0; i < dest.Length; i++)
                {
                    LightingBGFX r = EntityManager.GetComponentData<LightingBGFX>(dest[i].e);
                    r.fogColor = srgbColors ? Color.LinearToSRGB(fog.color) : fog.color;
                    float linearFogRange = fog.endDistance - fog.startDistance;
                    Assert.IsTrue(linearFogRange > 0.0f);
                    r.fogParams = new float4((float)fog.mode, fog.density, fog.endDistance, 1.0f / linearFogRange);
                    EntityManager.SetComponentData<LightingBGFX>(dest[i].e, r);
                }
            }).Run();

            Entities.WithoutBurst().ForEach((Entity e, ref LightingBGFX r) =>
            {
                if (r.numPointOrDirLights + r.numMappedLights <= 0)
                    RenderDebug.LogFormat("Warning: No lights found for lighting setup {0}.", e);
            }).Run();
        }
    }
}
