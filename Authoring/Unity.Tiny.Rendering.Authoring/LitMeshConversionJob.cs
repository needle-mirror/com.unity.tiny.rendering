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
    internal struct LitMeshConversionJob : IJob
    {
        public NativeArray<BlobAssetReference<LitMeshData>> BlobAssets; //Blob asset array shared across job instances and Deallocated from MeshConversion
        public UMeshSettings MeshSettings;
        [DeallocateOnJobCompletion] public NativeArray<Vector3> Positions;
        [DeallocateOnJobCompletion] public NativeArray<Vector2> UVs;
        [DeallocateOnJobCompletion] public NativeArray<Vector3> Normals;
        [DeallocateOnJobCompletion] public NativeArray<Vector3> Tangents;
        [DeallocateOnJobCompletion] public NativeArray<Vector3> BiTangents;
        [DeallocateOnJobCompletion] public NativeArray<Vector4> BoneWeights;
        [DeallocateOnJobCompletion] public NativeArray<Vector4> BoneIndices;
        [DeallocateOnJobCompletion] public NativeArray<Color> Colors;
        [DeallocateOnJobCompletion] public NativeArray<ushort> Indices;

        public LitMeshConversionJob(UMeshSettings settings, UMeshDataCache data, NativeArray<BlobAssetReference<LitMeshData>> blobArray)
        {
            MeshSettings = settings;
            Positions = data.uPositions;
            UVs = data.uUVs;
            Normals = data.uNormals;
            Tangents = data.uTangents;
            BiTangents = data.uBiTangents;
            BoneWeights = data.uBoneWeights;
            BoneIndices = data.uBoneIndices;
            Colors = data.uColors;
            Indices = data.uIndices;
            BlobAssets = blobArray;
        }

        public unsafe void CheckVertexLayout()
        {
            LitVertex tv;
            LitVertex* p = &tv;
            {
                Debug.Assert((long)&(p->Position) - (long)p == 0);
                Debug.Assert((long)&(p->TexCoord0) - (long)p == 12);
                Debug.Assert((long)&(p->Normal) - (long)p == 20);
                Debug.Assert((long)&(p->Tangent) - (long)p == 32);
                Debug.Assert((long)&(p->BoneWeight) - (long)p == 44);
                Debug.Assert((long)&(p->BoneIndex) - (long)p == 60);
                Debug.Assert((long)&(p->BillboardPos) - (long)p == 76);
                Debug.Assert((long)&(p->Albedo_Opacity) - (long)p == 88);
                Debug.Assert((long)&(p->Metal_Smoothness) - (long)p == 104);
            }
        }

        public void Execute()
        {
            CheckVertexLayout();

            var allocator = new BlobBuilder(Allocator.Temp);
            var settings = MeshSettings;
            ref var root = ref allocator.ConstructRoot<LitMeshData>();
            var vertices = allocator.Allocate(ref root.Vertices, Positions.Length);

            unsafe
            {
                int offset = 0;
                byte* dest = (byte*)vertices.GetUnsafePtr();
                //Copy vertices
                if (Positions.Length != 0)
                {
                    byte* positions = (byte*)(Positions.GetUnsafePtr<Vector3>());
                    UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), positions, sizeof(float3), sizeof(float3), Positions.Length);
                    offset += sizeof(float3);

                    byte* uvs = (byte*)UVs.GetUnsafePtr<Vector2>();
                    UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), uvs, sizeof(float2), sizeof(float2), Positions.Length);
                    offset += sizeof(float2);

                    byte* normals = (byte*)Normals.GetUnsafePtr<Vector3>();
                    UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), normals, sizeof(float3), sizeof(float3), Positions.Length);
                    offset += sizeof(float3);

                    byte* tangents = (byte*)Tangents.GetUnsafePtr<Vector3>();
                    UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), tangents, sizeof(float3), sizeof(float3), Positions.Length);
                    offset += sizeof(float3);

                    byte* boneWeights = (byte*)BoneWeights.GetUnsafePtr<Vector4>();
                    UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), boneWeights, sizeof(float4), sizeof(float4), Positions.Length);
                    offset += sizeof(float4);

                    byte* boneIndices = (byte*)BoneIndices.GetUnsafePtr<Vector4>();
                    UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), boneIndices, sizeof(float4), sizeof(float4), Positions.Length);
                    offset += sizeof(float4);
                }

                //Billboard position not present in UnityEngine.Mesh
                float3 billboardPos = float3.zero;
                UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), &billboardPos, 0, sizeof(float3), Positions.Length);
                offset += sizeof(float3);

                //Vertex color is not supported in URP lit shader, override to white for now
                float4 albedo = new float4(1);
                UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), &albedo, 0, sizeof(float4), Positions.Length);
                offset += sizeof(float4);

                //Vertex metal smoothness are not present in UnityEngine.Mesh
                float2 metal = new float2(1);
                UnsafeUtility.MemCpyStride(dest + offset, sizeof(LitVertex), &metal, 0, sizeof(float2), Positions.Length);

                //Copy indices
                if (Indices.Length != 0)
                {
                    byte* indices = (byte*)Indices.GetUnsafePtr<ushort>();
                    var dIndices = allocator.Allocate(ref root.Indices, Indices.Length);
                    byte* desti = (byte*)dIndices.GetUnsafePtr();
                    UnsafeUtility.MemCpy(desti, indices, sizeof(ushort) * Indices.Length);
                }
            }
            BlobAssets[MeshSettings.blobIndex] = allocator.CreateBlobAssetReference<LitMeshData>(Allocator.Persistent);
            allocator.Dispose();
        }
    }
}
