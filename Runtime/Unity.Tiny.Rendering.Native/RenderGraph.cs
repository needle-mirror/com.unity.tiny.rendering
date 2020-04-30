using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Tiny;
using Unity.Tiny.Assertions;
using Unity.Tiny.Rendering;
using Bgfx;

namespace Unity.Tiny.Rendering
{
    public struct RenderNodeRef : IBufferElementData
    {
        public Entity e; // next to a RenderNode, this node depends on those other nodes
    }

    public struct RenderPassRef : IBufferElementData
    {
        public Entity e; // next to RenderNode, list of passes in this node
    }

    [System.Serializable]
    public struct RenderToPasses : ISharedComponentData
    {
        public Entity e; // shared on every renderer, points to an entity that has RenderToPassesEntry[] buffer
    }

    public struct RenderGroup : IComponentData
    {   // tag for a render group (optional)
        // next to it: optional object/world bounds
        // next to it: DynamicArray<RenderToPassesEntry>
    }

    public struct RenderToPassesEntry : IBufferElementData
    {
        public Entity e; // list of entities that have a RenderPass component, where the renderer will render to
    }

    public struct RenderNode : IComponentData
    {
        public bool alreadyAdded;
        // next to it, required: DynamicArray<RenderNodeRef>, dependencies
        // next to it, required: DynamicArray<RenderPassRef>, list of passes in node
    }

    public struct RenderNodePrimarySurface : IComponentData
    {
        // place next to a RenderNode, to mark it as a sink: recursively starts evaluating render graphs from here
    }

    public struct RenderNodeTexture : IComponentData
    {
        public Entity colorTexture;
        public Entity depthTexture;
        public RenderPassRect rect;
    }

    public struct RenderNodeCubemap : IComponentData
    {
        public Entity target;
        public int side;
    }

    public struct RenderNodeShadowMap : IComponentData
    {
        public Entity lightsource;
    }

    public struct RenderPassUpdateFromCamera : IComponentData
    {
        // frustum, clear color, and transforms will auto update from a camera entity
        public Entity camera; // must have Camera component
        public bool updateClear; // update clear state and color from camera as well, if not set, clear state will be left alone
    }

    public struct RenderPassUpdateFromBlitterAutoAspect : IComponentData
    {
        public Entity blitRenderer;
    }

    public struct RenderPassUpdateFromLight : IComponentData
    {
        // frustum and transforms will auto update from a light entity
        public Entity light; // must have Light component
    }

    public struct RenderPassUpdateFromCascade : IComponentData
    {
        // frustum and transforms will auto update from a camera entity
        public Entity light; // must have Light and CascadeShadowmappedLight component
        public int cascade;
    }

    public struct RenderPassCascade : IComponentData
    {
        public int cascade;
    }

    public struct RenderPassAutoSizeToNode : IComponentData
    {
        // convenience, place next to a RenderPass so it updates its size to match the node's size
        // the node must be either primary or have a target texture of some sort
    }

    public struct RenderPassClearColorFromBorder : IComponentData
    {
        // convenience, place next to a RenderPass so its clear color is updated from DisplayInfo.backgroundBorderColor
    }

    [Flags]
    public enum RenderPassClear : ushort
    {
        Color = bgfx.ClearFlags.Color,
        Depth = bgfx.ClearFlags.Depth,
        Stencil = bgfx.ClearFlags.Stencil
    }

    public enum RenderPassSort: ushort
    {
        Unsorted = bgfx.ViewMode.Default,
        SortZLess = bgfx.ViewMode.DepthDescending,
        SortZGreater = bgfx.ViewMode.DepthAscending,
        Sorted = bgfx.ViewMode.Sequential
    }

    [Flags]
    public enum RenderPassType : uint
    {
        ZOnly = 1,
        Opaque = 2,
        Transparent = 4,
        UI = 8,
        FullscreenQuad = 16,
        ShadowMap = 32,
        Sprites = 64,
        DebugOverlay = 128,
        Clear = 256
    }

    public struct RenderPassRect
    {
        public ushort x, y, w, h;
    }

    public enum RenderPassFlags : uint
    {
        FlipCulling = 3,
        CullingMask = 3,
        RenderToTexture = 4
    }

    public struct RenderPass : IComponentData
    {
        public Entity inNode;
        public RenderPassSort sorting;
        public float4x4 projectionTransform;
        public float4x4 viewTransform;
        public RenderPassType passType;
        public ushort viewId;
        public RenderPassRect scissor;
        public RenderPassRect viewport;
        public RenderPassClear clearFlags;  // matches bgfx
        public uint clearRGBA;              // clear color, packed in bgfx format
        public float clearDepth;            // matches bgfx
        public byte clearStencil;           // matches bgfx
        public RenderPassFlags passFlags; // flags not used by bgfx, used internally
        public Frustum frustum; // Frustum for late stage culling

        // next to it, optional, Frustum for late stage culling
        public byte GetFlipCulling() { return (byte)(passFlags & RenderPassFlags.CullingMask); }
        public byte GetFlipCullingInverse() { return (byte)((passFlags & RenderPassFlags.CullingMask) ^ RenderPassFlags.CullingMask); }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(RendererBGFXSystem))]
    [UpdateAfter(typeof(UpdateCameraMatricesSystem))]
    [UpdateAfter(typeof(UpdateLightMatricesSystem))]
    [UpdateBefore(typeof(SubmitSystemGroup))]
    public unsafe class PreparePassesSystem : SystemBase
    {
        private void RecAddPasses(Entity eNode, ref ushort nextViewId)
        {
            // check already added
            RenderNode node = EntityManager.GetComponentData<RenderNode>(eNode);
            if (node.alreadyAdded)
                return;
            node.alreadyAdded = true;
            // recurse dependencies
            if (EntityManager.HasComponent<RenderNodeRef>(eNode))
            {
                DynamicBuffer<RenderNodeRef> deps = EntityManager.GetBuffer<RenderNodeRef>(eNode);
                for (int i = 0; i < deps.Length; i++)
                    RecAddPasses(deps[i].e, ref nextViewId);
            }
            // now add own passes
            if (EntityManager.HasComponent<RenderPassRef>(eNode))
            {
                DynamicBuffer<RenderPassRef> passes = EntityManager.GetBuffer<RenderPassRef>(eNode);
                //RenderDebug.LogFormat("Adding passes to graph for {0}: {1} passes.", eNode, passes.Length);
                for (int i = 0; i < passes.Length; i++)
                {
                    var p = EntityManager.GetComponentData<RenderPass>(passes[i].e);
                    p.viewId = nextViewId++;
                    EntityManager.SetComponentData<RenderPass>(passes[i].e, p);
                }
            }
        }

        protected override void OnCreate()
        {
        }

        protected override void OnUpdate()
        {
            var bgfxinst = World.GetExistingSystem<RendererBGFXSystem>().InstancePointer();
            if (!bgfxinst->m_initialized)
                return;

            // make sure passes have viewid, transform, scissor rect and view rect set

            // reset alreadyAdded state
            // we expect < 100 or so passes, so the below code does not need to be crazy great
            Entities.ForEach((ref RenderNode rnode) => { rnode.alreadyAdded = false; }).Run();
            Entities.ForEach((ref RenderPass pass) => { pass.viewId = 0xffff; }).Run(); // there SHOULD not be any passes around that are not referenced by the graph...

            // get all nodes, sort (bgfx issues in-order per view. a better api could use the render graph to issue without gpu
            // barriers where possible)
            // sort into eval order, assign pass viewId
            ushort nextViewId = 0;
            Entities.WithoutBurst().WithAll<RenderNodePrimarySurface>().ForEach((Entity eNode) => { RecAddPasses(eNode, ref nextViewId); }).Run();

            var di = World.TinyEnvironment().GetConfigData<DisplayInfo>();

            Entities.WithoutBurst().WithAll<RenderPassAutoSizeToNode>().ForEach((Entity e, ref RenderPass pass) =>
            {
                if (EntityManager.HasComponent<RenderNodePrimarySurface>(pass.inNode))
                {
                    pass.viewport.x = 0;
                    pass.viewport.y = 0;
                    pass.viewport.w = (ushort)di.framebufferWidth;
                    pass.viewport.h = (ushort)di.framebufferHeight;
                    return;
                }
                if (EntityManager.HasComponent<RenderNodeTexture>(pass.inNode))
                {
                    var texRef = EntityManager.GetComponentData<RenderNodeTexture>(pass.inNode);
                    pass.viewport = texRef.rect;
                }
                // TODO: add others like cubemap
            }).Run();

            // auto update passes that are matched with a camera
            Entities.WithoutBurst().ForEach((Entity e, ref RenderPass pass, ref RenderPassUpdateFromCamera fromCam) =>
            {
                Entity eCam = fromCam.camera;
                Camera cam = EntityManager.GetComponentData<Camera>(eCam);
                CameraMatrices camData = EntityManager.GetComponentData<CameraMatrices>(eCam);
                pass.viewTransform = camData.view;
                pass.projectionTransform = camData.projection;
                pass.frustum = camData.frustum;
                if (fromCam.updateClear)
                {
                    switch (cam.clearFlags)
                    {
                        default:
                        case CameraClearFlags.SolidColor:
                            pass.clearFlags = RenderPassClear.Color | RenderPassClear.Depth;
                            break;
                        case CameraClearFlags.DepthOnly:
                            pass.clearFlags = RenderPassClear.Depth;
                            break;
                        case CameraClearFlags.Nothing:
                            pass.clearFlags = 0;
                            break;
                    }
                    float4 cc = cam.backgroundColor.AsFloat4();
                    if (di.colorSpace == ColorSpace.Gamma)
                        cc = Color.LinearToSRGB(cc);
                    pass.clearRGBA = RendererBGFXStatic.PackColorBGFX(cc);
                }
            }).Run();

            Entities.WithAll<RenderPassClearColorFromBorder>().ForEach((Entity e, ref RenderPass pass) => {
                float4 cc = di.backgroundBorderColor.AsFloat4();
                if (di.colorSpace == ColorSpace.Gamma)
                    cc = Color.LinearToSRGB(cc);
                pass.clearRGBA = RendererBGFXStatic.PackColorBGFX(cc);
            }).Run();

            // auto update passes that are matched with a cascade
            Entities.WithoutBurst().ForEach((Entity e, ref RenderPass pass, ref RenderPassUpdateFromCascade fromCascade) => {
                Entity eLight = fromCascade.light;
                CascadeShadowmappedLightCache csmData = EntityManager.GetComponentData<CascadeShadowmappedLightCache>(eLight);
                CascadeData cs = csmData.GetCascadeData(fromCascade.cascade);
                pass.viewTransform = cs.view;
                pass.projectionTransform = cs.proj;
                pass.frustum = cs.frustum;
            }).Run();

            // auto update passes that are matched with a light
            Entities.WithoutBurst().ForEach((Entity e, ref RenderPass pass, ref RenderPassUpdateFromLight fromLight) => {
                Entity eLight = fromLight.light;
                LightMatrices lightData = EntityManager.GetComponentData<LightMatrices>(eLight);
                pass.viewTransform = lightData.view;
                pass.projectionTransform = lightData.projection;
                pass.frustum = lightData.frustum;
            }).Run();

            // set model matrix for blitting to automatically match texture aspect
            Entities.WithoutBurst().ForEach((Entity e, ref RenderPass pass, ref RenderPassUpdateFromBlitterAutoAspect b) => {
                var br = EntityManager.GetComponentData<BlitRenderer>(b.blitRenderer);
                var im2d = EntityManager.GetComponentData<Image2D>(br.texture);
                float srcAspect = (float)im2d.imagePixelWidth / (float)im2d.imagePixelHeight;
                float4x4 m = float4x4.identity;
                float destAspect = (float)pass.viewport.w / (float)pass.viewport.h;
                if (destAspect <= srcAspect)   // flip comparison to zoom in instead of black bars
                {
                    m.c0.x = 1.0f; m.c1.y = destAspect / srcAspect;
                }
                else
                {
                    m.c0.x = srcAspect / destAspect; m.c1.y = 1.0f;
                }
                pass.viewTransform = m;
            }).Run();

            // set up extra pass data
            Entities.WithoutBurst().ForEach((Entity e, ref RenderPass pass) =>
            {
                if (pass.viewId == 0xffff)
                {
                    RenderDebug.LogFormat("Render pass entity {0} on render node entity {1} is not referenced by the render graph. It should be deleted.", e, pass.inNode);
                    Assert.IsTrue(false);
                    return;
                }
                bool rtt = EntityManager.HasComponent<FramebufferBGFX>(pass.inNode);
                if (rtt) pass.passFlags = RenderPassFlags.RenderToTexture;
                else pass.passFlags = 0;
                // those could be more shared ... (that is, do all passes really need a copy of view & projection?)
                unsafe { fixed(float4x4 * viewp = &pass.viewTransform, projp = &pass.projectionTransform) {
                             if (bgfxinst->m_homogeneousDepth && bgfxinst->m_originBottomLeft) // gl style
                             {
                                 bgfx.set_view_transform(pass.viewId, viewp, projp);
                                 pass.passFlags &= ~RenderPassFlags.FlipCulling;
                             }
                             else // dx style
                             {
                                 bool yflip = !bgfxinst->m_originBottomLeft && rtt;
                                 float4x4 adjustedProjection = RendererBGFXStatic.AdjustProjection(ref pass.projectionTransform, !bgfxinst->m_homogeneousDepth, yflip);
                                 bgfx.set_view_transform(pass.viewId, viewp, &adjustedProjection);
                                 if (yflip) pass.passFlags |= RenderPassFlags.FlipCulling;
                                 else  pass.passFlags &= ~RenderPassFlags.FlipCulling;
                             }
                         }}
                bgfx.set_view_mode(pass.viewId, (bgfx.ViewMode)pass.sorting);
                bgfx.set_view_rect(pass.viewId, pass.viewport.x, pass.viewport.y, pass.viewport.w, pass.viewport.h);
                bgfx.set_view_scissor(pass.viewId, pass.scissor.x, pass.scissor.y, pass.scissor.w, pass.scissor.h);
                bgfx.set_view_clear(pass.viewId, (ushort)pass.clearFlags, pass.clearRGBA, pass.clearDepth, pass.clearStencil);
                if (rtt)
                {
                    var rttbgfx = EntityManager.GetComponentData<FramebufferBGFX>(pass.inNode);
                    bgfx.set_view_frame_buffer(pass.viewId, rttbgfx.handle);
                }
                else
                {
                    bgfx.set_view_frame_buffer(pass.viewId, new bgfx.FrameBufferHandle { idx = 0xffff });
                }
                // touch it? needed?
                bgfx.touch(pass.viewId);
            }).Run();
        }
    }
}
