using Bgfx;
using Unity.Build.DotsRuntime;
using Unity.Entities;
using Unity.Entities.Runtime.Build;
using Unity.Tiny.Rendering;
using Unity.Tiny.Rendering.Settings;

namespace Unity.TinyConversion
{
    [DisableAutoCreation]
    internal class DefaultShaderExportSystem : ShaderExportSystem
    {
        static readonly string kBinaryShaderFolderPath = "Packages/com.unity.tiny.rendering/Runtime/Unity.Tiny.Rendering.Native/shaderbin~/";

        protected override void OnUpdate()
        {
            if (BuildConfiguration == null)
                return;
            if (!BuildConfiguration.TryGetComponent<DotsRuntimeBuildProfile>(out var profile))
                return;
            if (!AssemblyCache.HasType<PrecompiledShaderData>())
                return;

            bool includeAllPlatform = false;
            if (BuildConfiguration.TryGetComponent<TinyShaderSettings>(out var shaderSettings))
            {
                includeAllPlatform = shaderSettings.PackageShadersForAllPlatforms;
            }

            bgfx.RendererType[] types = GetShaderFormat(profile.Target, includeAllPlatform);

            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.simple, "simple", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.simplelit, "simplelit", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.simplelitgpuskinning, "simplelitgpuskinning", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.line, "line", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.zOnly, "zOnly", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.blitsrgb, "blitsrgb", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.shadowmap, "shadowmap", types);
            CreateShaderDataEntity(kBinaryShaderFolderPath, ShaderType.shadowmapgpuskinning, "shadowmapgpuskinning", types);
        }
    }
}
