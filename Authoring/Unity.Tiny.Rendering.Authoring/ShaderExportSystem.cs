using System;
using System.IO;
using Bgfx;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Runtime.Build;
using Unity.Build.DotsRuntime;
using Unity.Tiny.Rendering;
using UnityEngine;

namespace Unity.TinyConversion
{
    public abstract class ShaderExportSystem : ConfigurationSystemBase
    {
        string GetShaderFileName(string shaderRootPath, string prefix, string shaderName, bgfx.RendererType backend)
        {
            var root = Path.GetFullPath(shaderRootPath);
            var filename = prefix + "_" + shaderName + "_" + backend + ".raw";
            return Path.Combine(root, filename.ToLower());
        }

        unsafe BlobAssetReference<PrecompiledShaderData> AddShaderData(string shaderRootPath, string shaderName, bgfx.RendererType[] types, string prefix)
        {
            using (var allocator = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref allocator.ConstructRoot<PrecompiledShaderData>();
                foreach (bgfx.RendererType sl in types)
                {
                    string fsFileName = GetShaderFileName(shaderRootPath, prefix, shaderName, sl);
                    var bytes = File.ReadAllBytes(fsFileName);
                    fixed(byte* data = bytes)
                    {
                        byte* dest = (byte*)allocator.Allocate(ref root.DataForBackend(sl), bytes.Length)
                            .GetUnsafePtr();
                        UnsafeUtility.MemCpy(dest, data, bytes.Length);
                    }
                }
                return allocator.CreateBlobAssetReference<PrecompiledShaderData>(Allocator.Persistent);
            }
        }

        protected bgfx.RendererType[] GetShaderFormat(BuildTarget target, bool forceIncludeAllPlatform = false)
        {
            // TODO: provide more customized options
            if (forceIncludeAllPlatform)
            {
                return new bgfx.RendererType[] { bgfx.RendererType.Metal, bgfx.RendererType.OpenGL, bgfx.RendererType.OpenGLES, bgfx.RendererType.Vulkan};
            }

            // TODO we need to move ths logic into the platforms packages; they should ultimately determine what shader types are needed
            var targetName = target.UnityPlatformName;
            // these are shader types, even though we reuse the renderer type enum.  d3d12 uses d3d11 shaders.
            if (targetName == UnityEditor.BuildTarget.StandaloneWindows.ToString() ||
                targetName == UnityEditor.BuildTarget.StandaloneWindows64.ToString())
                return new bgfx.RendererType[] { bgfx.RendererType.OpenGL, bgfx.RendererType.Direct3D9, bgfx.RendererType.Direct3D11, bgfx.RendererType.Vulkan };
            if (targetName == UnityEditor.BuildTarget.StandaloneLinux64.ToString())
                return new bgfx.RendererType[] { bgfx.RendererType.OpenGL, bgfx.RendererType.Vulkan };
            if (targetName == UnityEditor.BuildTarget.StandaloneOSX.ToString())
                return new bgfx.RendererType[] { bgfx.RendererType.OpenGL, bgfx.RendererType.Metal, bgfx.RendererType.Vulkan };
            // TODO: get rid of OpenGLES for iOS when problem with Metal on A7/A8 based devices is fixed
            if (targetName == UnityEditor.BuildTarget.iOS.ToString())
                return new bgfx.RendererType[] { bgfx.RendererType.OpenGL, bgfx.RendererType.OpenGLES, bgfx.RendererType.Metal };
            if (targetName == UnityEditor.BuildTarget.Android.ToString())
                return new bgfx.RendererType[] { bgfx.RendererType.OpenGL, bgfx.RendererType.OpenGLES, bgfx.RendererType.Vulkan };
            if (targetName == UnityEditor.BuildTarget.WebGL.ToString())
                return new bgfx.RendererType[] { bgfx.RendererType.OpenGLES };

            //TODO: Should we default to a specific shader type?
            throw new InvalidOperationException($"Target: {targetName} is not supported. No shaders will be exported");
        }

        protected Entity CreateShaderDataEntity(string rootShaderPath, Guid shaderGuid, string shaderName, bgfx.RendererType[] backends)
        {
            var e = EntityManager.CreateEntity(typeof(PrecompiledShader), typeof(VertexShaderBinData), typeof(FragmentShaderBinData));
            EntityManager.SetComponentData(e, new PrecompiledShader()
            {
                Guid = shaderGuid,
                Name = shaderName
            });
            EntityManager.SetComponentData(e, new VertexShaderBinData()
            {
                data = AddShaderData(rootShaderPath, shaderName, backends, "vs")
            });
            EntityManager.SetComponentData(e, new FragmentShaderBinData()
            {
                data = AddShaderData(rootShaderPath, shaderName, backends, "fs")
            });
            return e;
        }
    }
}
