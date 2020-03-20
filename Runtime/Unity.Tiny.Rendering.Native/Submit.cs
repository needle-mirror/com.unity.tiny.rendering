using System;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Tiny;
using Unity.Tiny.Assertions;
using Unity.Tiny.Rendering;
using Bgfx;
using System.Runtime.InteropServices;
using Unity.Transforms;
using Unity.Jobs;
using Unity.Burst;

namespace Unity.Tiny.Rendering
{
    [UpdateInGroup(typeof(SubmitSystemGroup))]
    public unsafe class SubmitBlitters : SystemBase
    {
        protected override void OnUpdate()
        {
            var sys = World.GetExistingSystem<RendererBGFXSystem>().InstancePointer();
            if (!sys->m_initialized)
                return;
            Dependency.Complete();

            Entities.WithoutBurst().ForEach((Entity e, ref BlitRenderer br) =>
            {
                if (!EntityManager.HasComponent<RenderToPasses>(e))
                    return;
                if (!EntityManager.HasComponent<TextureBGFX>(br.texture))
                    return;
                RenderToPasses toPassesRef = EntityManager.GetSharedComponentData<RenderToPasses>(e);
                DynamicBuffer<RenderToPassesEntry> toPasses = EntityManager.GetBufferRO<RenderToPassesEntry>(toPassesRef.e);
                var tex = EntityManager.GetComponentData<TextureBGFX>(br.texture);
                float4x4 idm = float4x4.identity;
                for (int i = 0; i < toPasses.Length; i++) {
                    Entity ePass = toPasses[i].e;
                    var pass = EntityManager.GetComponentData<RenderPass>(ePass);
                    if (sys->m_blitPrimarySRGB) {
                        // need to convert linear to srgb if we are not rendering to a texture in linear workflow
                        bool toPrimaryWithSRGB = EntityManager.HasComponent<RenderNodePrimarySurface>(pass.inNode) && sys->m_allowSRGBTextures;
                        if (!toPrimaryWithSRGB)
                            SubmitHelper.SubmitBlitDirectFast(sys, pass.viewId, ref idm, br.color, tex.handle);
                        else
                            SubmitHelper.SubmitBlitDirectExtended(sys, pass.viewId,  ref idm, tex.handle,
                                false, true, 0.0f, new float4(1.0f), new float4(0.0f), false);
                    } else {
                        SubmitHelper.SubmitBlitDirectFast(sys, pass.viewId, ref idm, br.color, tex.handle);
                    }
                }
            }).Run();
        }
    }

    [UpdateInGroup(typeof(SubmitSystemGroup))]
    public unsafe class SubmitSimpleMesh : SystemBase
    {
        protected override void OnUpdate()
        {
            Dependency.Complete();
            var sys = World.GetExistingSystem<RendererBGFXSystem>().InstancePointer();
            if (!sys->m_initialized)
                return;
            // get all MeshRenderer, cull them, and add them to graph nodes that need them 
            // any mesh renderer MUST have a shared component data that has a list of passes to render to
            // this list is usually very shared - all opaque meshes will render to all ZOnly and Opaque passes
            // this shared data is not dynamically updated - other systems are responsible to update them if needed
            // simple
            Entities.WithAll<SimpleMeshRenderer>().WithoutBurst().ForEach((Entity e, ref MeshRenderer mr, ref LocalToWorld tx, ref WorldBounds wb, ref WorldBoundingSphere wbs) =>
            {
                if (!EntityManager.HasComponent<RenderToPasses>(e))
                    return;

                RenderToPasses toPassesRef = EntityManager.GetSharedComponentData<RenderToPasses>(e);
                DynamicBuffer<RenderToPassesEntry> toPasses = EntityManager.GetBufferRO<RenderToPassesEntry>(toPassesRef.e);
                for (int i = 0; i < toPasses.Length; i++) {
                    Entity ePass = toPasses[i].e;
                    var pass = EntityManager.GetComponentData<RenderPass>(ePass);
                    if (Culling.Cull(ref wbs, ref pass.frustum) == Culling.CullingResult.Outside)
                        continue;
                    // double cull as example only
                    if (Culling.IsCulled(ref wb, ref pass.frustum))
                        continue;
                    var mesh = EntityManager.GetComponentData<MeshBGFX>(mr.mesh);
                    switch (pass.passType) {
                        case RenderPassType.ZOnly:
                            SubmitHelper.SubmitZOnlyDirect(sys, pass.viewId, ref mesh, ref tx.Value, mr.startIndex, mr.indexCount, pass.GetFlipCulling());
                            break;
                        case RenderPassType.ShadowMap:
                            SubmitHelper.SubmitZOnlyDirect(sys, pass.viewId, ref mesh, ref tx.Value, mr.startIndex, mr.indexCount, pass.GetFlipCullingInverse());
                            break;
                        case RenderPassType.Transparent:
                        case RenderPassType.Opaque:
                            var material = EntityManager.GetComponentData<SimpleMaterialBGFX>(mr.material);
                            SubmitHelper.SubmitSimpleDirect(sys, pass.viewId, ref mesh, ref tx.Value, ref material, mr.startIndex, mr.indexCount, pass.GetFlipCulling());
                            break;
                        default:
                            Assert.IsTrue(false);
                            break;
                    }
                }
            }).Run();
        }
    }

    [UpdateInGroup(typeof(SubmitSystemGroup))]
    public class SubmitStaticLitMeshChunked : SystemBase
    {
        unsafe struct SubmitStaticLitMeshJob : IJobChunk
        {
            [ReadOnly] public ArchetypeChunkComponentType<LocalToWorld> LocalToWorldType;
            [ReadOnly] public ArchetypeChunkComponentType<MeshRenderer> MeshRendererType;
            [ReadOnly] public ArchetypeChunkComponentType<WorldBounds> WorldBoundsType;
            [ReadOnly] public ArchetypeChunkComponentType<WorldBoundingSphere> WorldBoundingSphereType;
            [ReadOnly] public ArchetypeChunkComponentType<ChunkWorldBoundingSphere> ChunkWorldBoundingSphereType;
            [ReadOnly] public ArchetypeChunkComponentType<ChunkWorldBounds> ChunkWorldBoundsType;
            [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<Entity> SharedRenderToPass;
            [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<Entity> SharedLightingRef;
            [ReadOnly] public BufferFromEntity<RenderToPassesEntry> BufferRenderToPassesEntry;
            [ReadOnly] public ComponentDataFromEntity<MeshBGFX> ComponentMeshBGFX;
            [ReadOnly] public ComponentDataFromEntity<RenderPass> ComponentRenderPass;
            [ReadOnly] public ComponentDataFromEntity<LitMaterialBGFX> ComponentLitMaterialBGFX;
            [ReadOnly] public ComponentDataFromEntity<LightingBGFX> ComponentLightingBGFX;
#pragma warning disable 0649
            [NativeSetThreadIndex] internal int ThreadIndex;
#pragma warning restore 0649
            [ReadOnly] public PerThreadDataBGFX* PerThreadData;
            [ReadOnly] public int MaxPerThreadData;
            [ReadOnly] public RendererBGFXInstance* BGFXInstancePtr;

            public unsafe void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var chunkLocalToWorld = chunk.GetNativeArray(LocalToWorldType);
                var chunkMeshRenderer = chunk.GetNativeArray(MeshRendererType);
                var worldBoundingSphere = chunk.GetNativeArray(WorldBoundingSphereType);
                var chunkWorldBoundingSphere = chunk.GetChunkComponentData(ChunkWorldBoundingSphereType).Value;
                var bounds = chunk.GetChunkComponentData(ChunkWorldBoundsType).Value;

                Assert.IsTrue (chunk.HasChunkComponent(ChunkWorldBoundingSphereType));

                Entity lighte = SharedLightingRef[chunkIndex];
                var lighting = ComponentLightingBGFX[lighte];
                Entity rtpe = SharedRenderToPass[chunkIndex];

                Assert.IsTrue(ThreadIndex >= 0 && ThreadIndex < MaxPerThreadData);
                bgfx.Encoder* encoder = PerThreadData[ThreadIndex].encoder;
                if (encoder == null) {
                    encoder = bgfx.encoder_begin(true);
                    Assert.IsTrue(encoder != null);
                    PerThreadData[ThreadIndex].encoder = encoder;
                }
                DynamicBuffer<RenderToPassesEntry> toPasses = BufferRenderToPassesEntry[rtpe];

                // we can do this loop either way, passes first or renderers first. 
                // TODO: profile what is better!
                for (int i = 0; i < toPasses.Length; i++) { // for all passes this chunk renderer to 
                    Entity ePass = toPasses[i].e;
                    var pass = ComponentRenderPass[ePass];
                    Assert.IsTrue(encoder != null);                    
                    for (int j = 0; j < chunk.Count; j++) { // for every renderer in chunk
                        var wbs = worldBoundingSphere[j];
                        var tx = chunkLocalToWorld[j].Value;
                        if (wbs.radius > 0.0f && Culling.Cull(ref wbs, ref pass.frustum) == Culling.CullingResult.Outside) // TODO: fine cull only if rough culling was !Inside
                            continue;
                        var meshRenderer = chunkMeshRenderer[j];
                        if (meshRenderer.indexCount > 0 && ComponentMeshBGFX.Exists(meshRenderer.mesh)) { 
                            var mesh = ComponentMeshBGFX[meshRenderer.mesh];
                            Assert.IsTrue(mesh.IsValid());
                            switch (pass.passType) { // TODO: we can hoist this out of the loop 
                                case RenderPassType.ZOnly:
                                    SubmitHelper.EncodeZOnly(BGFXInstancePtr, encoder, pass.viewId, ref mesh, ref tx, meshRenderer.startIndex, meshRenderer.indexCount, pass.GetFlipCulling());
                                    break;
                                case RenderPassType.ShadowMap:
                                    float4 bias = new float4(0);
                                    SubmitHelper.EncodeShadowMap(BGFXInstancePtr, encoder, pass.viewId, ref mesh, ref tx, meshRenderer.startIndex, meshRenderer.indexCount, pass.GetFlipCullingInverse(), bias);
                                    break;
                                case RenderPassType.Transparent:
                                case RenderPassType.Opaque:
                                    var material = ComponentLitMaterialBGFX[meshRenderer.material];
                                    SubmitHelper.EncodeLit(BGFXInstancePtr, encoder, pass.viewId, ref mesh, ref tx, ref material, ref lighting, ref pass.viewTransform, meshRenderer.startIndex, meshRenderer.indexCount, pass.GetFlipCulling(), ref PerThreadData[ThreadIndex].viewSpaceLightCache);
                                    break;
                                default:
                                    Assert.IsTrue(false);
                                    break;
                            }
                        } else {
                            //var mesh = ComponentDynamicMeshBGFX[meshRenderer.mesh];
                        }
                    }
                }
            }
        }

        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(
                ComponentType.ReadOnly<LitMeshRenderer>(),
                ComponentType.ReadOnly<MeshRenderer>(),
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<WorldBounds>(),
                ComponentType.ReadOnly<WorldBoundingSphere>(),
                ComponentType.ChunkComponentReadOnly<ChunkWorldBoundingSphere>(),
                ComponentType.ChunkComponentReadOnly<ChunkWorldBounds>(),
                ComponentType.ReadOnly<RenderToPasses>()
            );
        }

        protected override void OnDestroy()
        {
        }

        protected unsafe override void OnUpdate()
        {
            var sys = World.GetExistingSystem<RendererBGFXSystem>().InstancePointer();
            if (!sys->m_initialized)
                return;

            var chunks = m_query.CreateArchetypeChunkArray(Allocator.Temp);
            NativeArray<Entity> sharedRenderToPass = new NativeArray<Entity>(chunks.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<Entity> sharedLightingRef = new NativeArray<Entity>(chunks.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            ArchetypeChunkSharedComponentType<RenderToPasses> renderToPassesType = GetArchetypeChunkSharedComponentType<RenderToPasses>();
            ArchetypeChunkSharedComponentType<LightingRef> lightingRefType = GetArchetypeChunkSharedComponentType<LightingRef>();

            // it really sucks we can't get shared components in the job itself
            for (int i = 0; i < chunks.Length; i++) {
                sharedRenderToPass[i] = chunks[i].GetSharedComponentData<RenderToPasses>(renderToPassesType, EntityManager).e;
                sharedLightingRef[i] = chunks[i].GetSharedComponentData<LightingRef>(lightingRefType, EntityManager).e;
            }
            chunks.Dispose();

            var encodejob = new SubmitStaticLitMeshJob {
                LocalToWorldType = GetArchetypeChunkComponentType<LocalToWorld>(true),
                MeshRendererType = GetArchetypeChunkComponentType<MeshRenderer>(true),
                WorldBoundsType = GetArchetypeChunkComponentType<WorldBounds>(true),
                WorldBoundingSphereType = GetArchetypeChunkComponentType<WorldBoundingSphere>(true),
                ChunkWorldBoundingSphereType = GetArchetypeChunkComponentType<ChunkWorldBoundingSphere>(true),
                ChunkWorldBoundsType = GetArchetypeChunkComponentType<ChunkWorldBounds>(true),
                SharedRenderToPass = sharedRenderToPass,
                SharedLightingRef = sharedLightingRef,
                BufferRenderToPassesEntry = GetBufferFromEntity<RenderToPassesEntry>(true),
                ComponentMeshBGFX = GetComponentDataFromEntity<MeshBGFX>(true),
                ComponentRenderPass = GetComponentDataFromEntity<RenderPass>(true),
                ComponentLitMaterialBGFX = GetComponentDataFromEntity<LitMaterialBGFX>(true),
                ComponentLightingBGFX = GetComponentDataFromEntity<LightingBGFX>(true),
                PerThreadData = sys->m_perThreadData,
                MaxPerThreadData = sys->m_maxPerThreadData,
                BGFXInstancePtr = sys
            };
            Assert.IsTrue(sys->m_maxPerThreadData>0 && encodejob.MaxPerThreadData>0);

            Dependency = encodejob.ScheduleParallel(m_query, Dependency);
            // Temporary workaround until dependencies bugs are fixed.
            Dependency.Complete();
        }
    }

}
