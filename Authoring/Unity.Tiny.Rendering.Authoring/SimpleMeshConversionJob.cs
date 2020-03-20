using Unity.Tiny.Rendering;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Unity.Jobs;
using Unity.Entities;
using Unity.Burst;

namespace Unity.TinyConversion
{
    [BurstCompile]
    internal struct SimpleMeshConversionJob : IJob
    {
        public NativeArray<BlobAssetReference<SimpleMeshData>> BlobAssets; //Blob asset array shared across job instances and Deallocated from MeshConversion
        public UMeshSettings MeshSettings;
        [DeallocateOnJobCompletion] public NativeArray<Vector3> Positions;
        [DeallocateOnJobCompletion] public NativeArray<Vector2> UVs;
        [DeallocateOnJobCompletion] public NativeArray<ushort> Indices;

        public SimpleMeshConversionJob(UMeshSettings settings, UMeshDataCache data, NativeArray<BlobAssetReference<SimpleMeshData>> blob)
        {
            MeshSettings = settings;
            Positions = data.uPositions;
            UVs = data.uUVs;
            Indices = data.uIndices;
            BlobAssets = blob;
        }

        public unsafe void CheckVertexLayout()
        {
            SimpleVertex tv;
            SimpleVertex* p = &tv;
            {
                Debug.Assert((long)&(p->Position) - (long)p == 0);
                Debug.Assert((long)&(p->TexCoord0) - (long)p == 12);
                Debug.Assert((long)&(p->Color) - (long)p == 20);
            }
        }

        public void Execute()
        {
            CheckVertexLayout();

            var allocator = new BlobBuilder(Allocator.Temp);
            var settings = MeshSettings;
            ref var root = ref allocator.ConstructRoot<SimpleMeshData>();

            var vertices = allocator.Allocate(ref root.Vertices, Positions.Length);

            unsafe
            {
                int offset = 0;
                byte* dest = (byte*)vertices.GetUnsafePtr();
                //Copy vertices
                if (Positions.Length != 0)
                {
                    byte* positions = (byte*)(Positions.GetUnsafePtr<Vector3>());
                    UnsafeUtility.MemCpyStride(dest + offset, sizeof(SimpleVertex), positions, sizeof(float3), sizeof(float3), Positions.Length);
                    offset += sizeof(float3);

                    byte* uvs = (byte*)UVs.GetUnsafePtr<Vector2>();
                    UnsafeUtility.MemCpyStride(dest + offset, sizeof(SimpleVertex), uvs, sizeof(float2), sizeof(float2), Positions.Length);
                    offset += sizeof(float2);
                }

                //Vertex color is not supported in URP lit shader, override to white for now
                float4 albedo = new float4(1);
                UnsafeUtility.MemCpyStride(dest + offset, sizeof(SimpleVertex), &albedo, 0, sizeof(float4), Positions.Length);

                //Copy indices
                if (Indices.Length != 0)
                {
                    byte* indices = (byte*)Indices.GetUnsafePtr<ushort>();
                    var dIndices = allocator.Allocate(ref root.Indices, Indices.Length);
                    byte* desti = (byte*)dIndices.GetUnsafePtr();
                    UnsafeUtility.MemCpy(desti, indices, sizeof(ushort) * Indices.Length);
                }
            }
            BlobAssets[MeshSettings.blobIndex] = allocator.CreateBlobAssetReference<SimpleMeshData>(Allocator.Persistent);
            allocator.Dispose();
        }
    }
}
