using Unity.Entities;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.Tiny.Rendering.Native")]
namespace Unity.Tiny.Rendering
{
    /// <summary>
    /// Simple Vertex data used with a Simple Shader
    /// </summary>
    public struct SimpleVertex
    {
        public float3 Position;
        public float2 TexCoord0;
        public float4 Color;
    }

    /// <summary>
    /// Single vertex data used with a Lit Shader
    /// </summary>
    public struct LitVertex
    {
        public float3 Position;
        public float2 TexCoord0;
        public float3 Normal;
        public float3 Tangent;          // TODO: float4 w is bitangent sign
        public float3 BiTangent;        // TODO: remove
        public float4 Albedo_Opacity;   // TODO: 8/16 bit packed 
        public float2 Metal_Smoothness; // TODO: 8/16 bit packed 
    }

    /// <summary>
    /// Mesh structure (used for 3D cases)
    /// This is a blob asset, reference by the LitMeshRenderData component 
    /// </summary>
    public struct LitMeshData
    {
        public BlobArray<ushort> Indices;
        public BlobArray<LitVertex> Vertices;
    }

    /// <summary>
    /// Simple mesh data. (Use with Simple shader and 2D cases)
    /// This is a blob asset, reference by the SimpleMeshRenderData component 
    /// </summary>
    public struct SimpleMeshData
    {
        public BlobArray<ushort> Indices;
        public BlobArray<SimpleVertex> Vertices;
    }

    /// <summary>
    /// Blob asset component to add to a mesh entity containg all mesh data to work with a lit shader
    /// Needs a MeshBounds next to it
    /// </summary>
    public struct LitMeshRenderData : IComponentData
    {
        public BlobAssetReference<LitMeshData> Mesh;
    }

    /// <summary>
    /// Blob asset component to add next to a mesh entity containing only vertex positions, colors and texture coordinates to work with a simple shader.
    /// Needs a MeshBounds next to it
    /// </summary>
    public struct SimpleMeshRenderData : IComponentData
    {
        public BlobAssetReference<SimpleMeshData> Mesh;
    }

    /// Place next to a buffer of DynamicLitVertex or DynamicSimpleVertex, and a buffer of DynamicIndex
    /// Needs a MeshBounds next to it
    public struct DynamicMeshData : IComponentData
    {
        public bool Dirty;                  // set to true to trigger re-upload, will revert to false after upload 
        public bool UseDynamicGPUBuffer;    // allocate a dynamic buffer on the gpu, only use this if you expect buffer contents to change every frame
        public int VertexCapacity;          // capacity for gpu buffer, must be >= NumVertices. Increasing capacity will require re-allocating buffers
        public int IndexCapacity;           // capacity for gpu buffer, must be >= NumIndices. Increasing capacity will require re-allocating buffers
        public int NumVertices;             // number of vertices to copy from the DynamicLitVertex or DynamicSimpleVertex buffer located next to this component 
        public int NumIndices;              // number of indices to copy from the DynamicIndex next to this component
    }

    /// Must be placed next to a SimpleMeshRenderData, LitMeshRenderData or DynamicMeshData
    public struct MeshBounds : IComponentData
    {
        public AABB Bounds; 
    }

    public struct DynamicLitVertex : IBufferElementData
    {
        public LitVertex Value;
    }

    public struct DynamicIndex : IBufferElementData
    {
        public ushort Value;
    }

    public struct DynamicSimpleVertex : IBufferElementData
    {
        public SimpleVertex Value;
    }

}