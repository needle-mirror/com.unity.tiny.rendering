using Unity.Build;
using Unity.Properties;

namespace Unity.Tiny.Rendering.Settings
{
    //TODO Need to find a way to retrieve project settings from runtime component without bringing a dependency to runtime packages
    public class TinyRenderingSettings : IBuildComponent
    {
        [CreateProperty]
        public int ResolutionX = 1280; //TODO: switch to VectorInt when there will be a Vector2Int Built-in inspector

        [CreateProperty]
        public int ResolutionY = 720;

        [CreateProperty]
        public bool AutoResizeFrame = true;

        [CreateProperty]
        public bool DisableVsync = false;
    }
}
