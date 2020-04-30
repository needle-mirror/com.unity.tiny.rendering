using Bgfx;
using Unity.Entities;
using Unity.Entities.Runtime.Build;
using Unity.Tiny.Rendering;

namespace Unity.TinyConversion
{
    [DisableAutoCreation]
    internal class DefaultShaderExportSystem : ShaderExportSystem
    {
        static readonly string kBinaryShaderFolderPath = "Packages/com.unity.tiny.rendering/Runtime/Unity.Tiny.Rendering.Native/shaderbin~/";

        protected override void OnUpdate()
        {
            if (buildConfiguration == null)
                return;
            if (!buildConfiguration.TryGetComponent<DotsRuntimeBuildProfile>(out var profile))
                return;
            if (!buildConfiguration.TryGetComponent<DotsRuntimeRootAssembly>(out var rootAssembly))
                return;
            if (!rootAssembly.TypeCache.HasType<PrecompiledShaderData>())
                return;

            bgfx.RendererType[] types = GetShaderFormat(profile.Target);

            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.simple, "simple", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.simplelit, "simplelit", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.line, "line", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.zOnly, "zOnly", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.blitsrgb, "blitsrgb", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.shadowmap, "shadowmap", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.sprite, "sprite", types);
        }
    }
}
