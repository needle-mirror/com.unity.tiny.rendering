using Unity.Entities;
using Unity.Entities.Runtime.Build;
using UnityEngine.Assertions;
using Unity.Tiny.Authoring;
using Unity.Tiny.Rendering.Settings;
using Unity.Tiny.Rendering;

namespace Unity.Tiny.Authoring
{
    [UpdateAfter(typeof(ConfigurationSystem))]
    [DisableAutoCreation]
    internal class ExportTinyRenderingSettings : ConfigurationSystemBase
    {
        protected override void OnUpdate()
        {
            using (var query = EntityManager.CreateEntityQuery(typeof(ConfigurationTag))) {
                int num = query.CalculateEntityCount();
                Assert.IsTrue(num != 0);
                var singletonEntity = query.GetSingletonEntity();
                DisplayInfo di = DisplayInfo.Default;
                RenderGraphConfig rc = RenderGraphConfig.Default;
                di.colorSpace = UnityEditor.PlayerSettings.colorSpace == UnityEngine.ColorSpace.Gamma ? ColorSpace.Gamma : ColorSpace.Linear;
                if (BuildConfiguration != null) {
                    if (BuildConfiguration.TryGetComponent<TinyRenderingSettings>(out var settings)) {
                        di.width = settings.WindowSize.x;
                        di.height = settings.WindowSize.y;
                        di.autoSizeToFrame = settings.AutoResizeFrame;
                        di.disableVSync = settings.DisableVsync;
                        di.gpuSkinning = settings.GPUSkinning;
                        rc.RenderBufferWidth = settings.RenderResolution.x;
                        rc.RenderBufferHeight = settings.RenderResolution.y;
                        rc.RenderBufferMaxSize = settings.MaxResolution;
                        rc.Mode = settings.RenderGraphMode;
                    } else {
                        UnityEngine.Debug.LogWarning($"The {nameof(TinyRenderingSettings)} build component is missing from the build configuration {BuildConfiguration.name}. Default rendering settings have been exported.");
                    }
                }
                EntityManager.AddComponentData(singletonEntity, di);
                EntityManager.AddComponentData(singletonEntity, rc);
            }
        }
    }
}
