using Unity.Entities;
using Unity.Entities.Runtime.Build;
using UnityEngine.Assertions;
using Unity.Tiny.Authoring;
using Unity.Tiny.Rendering.Settings;

namespace Unity.Tiny.Authoring
{
    [UpdateAfter(typeof(ConfigurationSystem))]
    public class ExportTinyRenderingSettings: ConfigurationSystemBase
    {
        protected override void OnUpdate()
        {
            using (var query = EntityManager.CreateEntityQuery(typeof(ConfigurationTag)))
            {
                int num = query.CalculateEntityCount();
                Assert.IsTrue(num != 0);
                var singletonEntity = query.GetSingletonEntity();
                DisplayInfo di = DisplayInfo.Default;
                if (buildSettings != null)
                {
                    if (buildSettings.TryGetComponent<TinyRenderingSettings>(out var settings))
                    {
                        di.width = settings.ResolutionX;
                        di.height = settings.ResolutionY;
                        di.autoSizeToFrame = settings.AutoResizeFrame;
                        di.disableSRGB = settings.DisableSRGB;
                        di.disableVSync = settings.DisableVsync;   
                    }
                    else
                        UnityEngine.Debug.LogWarning($"The {nameof(TinyRenderingSettings)} build component is missing from the build setting {buildSettings.name}. Default rendering settings have been exported.");
                }
                EntityManager.AddComponentData(singletonEntity, di);
            }
        }
    }
}
