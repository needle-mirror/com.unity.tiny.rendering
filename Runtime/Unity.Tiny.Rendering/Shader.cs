using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Tiny.Rendering
{
    public static class ShaderType
    {
        public static readonly Guid simple = new Guid("c3e8321c-7ca4-4f2d-bcb8-a097392bd5be");
        public static readonly Guid simplelit = new Guid("5d60ab81-52dc-455e-ab1c-7b8963242420");
        public static readonly Guid simplelitgpuskinning = new Guid("3506F263-556B-491B-BD2E-C3B59622F0A3");
        public static readonly Guid line = new Guid("03E8FA8A-F56E-49B7-94B4-8C6DFA8D4ED9");
        public static readonly Guid zOnly = new Guid("11DEAB35-8D18-4A0D-9D9C-8800B7E0FAF6");
        public static readonly Guid blitsrgb = new Guid("5876A499-59C7-46A7-A93B-6475C97745D5");
        public static readonly Guid externalblites3 = new Guid("2F00B069-1D12-4D14-8E63-7FD38846FD5C");
        public static readonly Guid shadowmap = new Guid("AFFC8771-429B-4546-B004-8069114518B7");
        public static readonly Guid shadowmapgpuskinning = new Guid("BC2BD45F-D168-4667-8911-E10BE12BC081");
    }

    public struct PrecompiledShader : IComponentData
    {
        public Guid Guid;
        public FixedString32 Name;
    }

    /// <summary>
    /// Shader data per shader language type
    /// </summary>
    public struct PrecompiledShaderData
    {
        public BlobArray<byte> dx9;
        public BlobArray<byte> dx11;
        public BlobArray<byte> metal;
        public BlobArray<byte> glsles;
        public BlobArray<byte> glsl;
        public BlobArray<byte> spirv;
    }

    /// <summary>
    /// Blob asset reference for each vertex and fragment shaders. To add next to each shader type
    /// </summary>
    public struct VertexShaderBinData : IComponentData
    {
        public BlobAssetReference<PrecompiledShaderData> data;
    }

    public struct FragmentShaderBinData : IComponentData
    {
        public BlobAssetReference<PrecompiledShaderData> data;
    }
}
