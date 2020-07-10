using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Tiny.Rendering
{
    public static class ShaderType
    {
        public static readonly Hash128 simple = new Hash128("c3e8321c7ca44f2dbcb8a097392bd5be");
        public static readonly Hash128 simplelit = new Hash128("5d60ab8152dc455eab1c7b8963242420");
        public static readonly Hash128 simplelitgpuskinning = new Hash128("3506F263556B491BBD2EC3B59622F0A3");
        public static readonly Hash128 line = new Hash128("03E8FA8AF56E49B794B48C6DFA8D4ED9");
        public static readonly Hash128 zOnly = new Hash128("11DEAB358D184A0D9D9C8800B7E0FAF6");
        public static readonly Hash128 blitsrgb = new Hash128("5876A49959C746A7A93B6475C97745D5");
        public static readonly Hash128 externalblites3 = new Hash128("2F00B0691D124D148E637FD38846FD5C");
        public static readonly Hash128 shadowmap = new Hash128("AFFC8771429B4546B0048069114518B7");
        public static readonly Hash128 shadowmapgpuskinning = new Hash128("BC2BD45FD16846678911E10BE12BC081");
    }

    public struct PrecompiledShader : IComponentData
    {
        public Hash128 Guid;
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
