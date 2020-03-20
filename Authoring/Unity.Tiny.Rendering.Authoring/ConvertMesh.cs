using System;
using Unity.Entities;
using Unity.Tiny.Rendering;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;
using Unity.Jobs;
using Unity.Entities.Runtime.Build;
using System.Collections.Generic;
using Unity.Assertions;

namespace Unity.TinyConversion
{
    internal struct UMeshSettings
    {
        public Hash128 hash;
        public int subMeshCount;
        public float3 center;
        public float3 extents;
        public int blobIndex;

        public UMeshSettings(Hash128 h, UnityEngine.Mesh uMesh, int i)
        {
            hash = h;
            subMeshCount = uMesh.subMeshCount;
            center = uMesh.bounds.center;
            extents = uMesh.bounds.extents;
            blobIndex = i;
        }
    }

    internal struct UMeshDataCache
    {
        public NativeArray<Vector3> uPositions;
        public NativeArray<Vector2> uUVs;
        public NativeArray<Vector3> uNormals;
        public NativeArray<Vector3> uTangents;
        public NativeArray<Vector3> uBiTangents;
        public NativeArray<Color> uColors;
        public NativeArray<ushort> uIndices;

        public unsafe void RetrieveSimpleMeshData(Mesh uMesh)
        {
            //Invert uvs
            var uvs = uMesh.uv;
            uUVs = new NativeArray<Vector2>(uvs.Length, Allocator.TempJob);
            for (int i = 0; i < uvs.Length; i++)
            {
                uvs[i].y = 1 - uvs[i].y;
                uUVs[i] = uvs[i];
            }

            var vertices = uMesh.vertices;
            uPositions = new NativeArray<Vector3>(vertices.Length, Allocator.TempJob);
            uPositions.CopyFrom(vertices);

            int indexCount = 0;
            for (int i = 0; i < uMesh.subMeshCount; i++)
            {
                indexCount += (int)uMesh.GetIndexCount(i);
            }

            int offset = 0;
            uIndices = new NativeArray<ushort>(indexCount, Allocator.TempJob);
            for (int i = 0; i < uMesh.subMeshCount; i++)
            {
                int[] indices = uMesh.GetIndices(i);
                for (int j = 0; j < indices.Length; j++)
                {
                    uIndices[offset + j] = Convert.ToUInt16(indices[j]);
                }
                offset += indices.Length;
            }
        }

        public unsafe void RetrieveLitMeshData(Mesh uMesh)
        {
            RetrieveSimpleMeshData(uMesh);

            Vector4[] tang4 = uMesh.tangents; //uMesh.tangents is vector4 with x,y,z components, and w used to flip the binormal.
            Vector3[] nor = uMesh.normals;
            Vector3[] biTang = new Vector3[tang4.Length];

            if (tang4.Length != nor.Length)
                UnityEngine.Debug.LogWarning($"The mesh {uMesh.name} should have the same number of normals {nor.Length} and tangents {tang4.Length}");

            uNormals = new NativeArray<Vector3>(nor.Length, Allocator.TempJob);
            uTangents = new NativeArray<Vector3>(nor.Length, Allocator.TempJob);
            uBiTangents = new NativeArray<Vector3>(nor.Length, Allocator.TempJob);

            for (int i = 0; i < Math.Min(tang4.Length, nor.Length); i++)
            {
                Vector3 tangent = tang4[i];
                Vector3 normal = nor[i];
                tangent.Normalize();
                normal.Normalize();

                // Orthogonalize
                tangent = tangent - normal * Vector3.Dot(normal, tangent);
                tangent.Normalize();

                // Fix T orientation
                if (Vector3.Dot(Vector3.Cross(normal, tangent), uBiTangents[i]) < 0.0f)
                {
                    tangent = tangent * -1.0f;
                }

                uBiTangents[i] = Vector3.Cross(normal, tangent) * tang4[i].w; // tang.w should be 1 or -1
                uNormals[i] = normal;
                uTangents[i] = tangent;
            }


            var colors = uMesh.colors;
            uColors = new NativeArray<Color>(colors.Length, Allocator.TempJob);
            uColors.CopyFrom(colors);
        }
    }

    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    public class MeshConversion : GameObjectConversionSystem
    {
        public override bool ShouldRunConversionSystem()
        {
            //Workaround for running the tiny conversion systems only if the BuildSettings have the DotsRuntimeBuildProfile component, so these systems won't run in play mode
            if (!TryGetBuildConfigurationComponent<DotsRuntimeBuildProfile>(out _))
                return false;
            return base.ShouldRunConversionSystem();
        }

        protected override void OnUpdate()
        {
            var simpleMeshContext = new BlobAssetComputationContext<UMeshSettings, SimpleMeshData>(BlobAssetStore, 128, Allocator.Temp);
            var litMeshContext = new BlobAssetComputationContext<UMeshSettings, LitMeshData>(BlobAssetStore, 128, Allocator.Temp);
            
            JobHandle combinedJH = new JobHandle();
            int simpleIndex = 0;
            int litIndex = 0;

            // Init blobasset arrays
            Entities.ForEach((UnityEngine.Mesh uMesh) =>
            {
                var entity = GetPrimaryEntity(uMesh);
                if (DstEntityManager.HasComponent<SimpleMeshRenderData>(entity))
                    simpleIndex++;
                if (DstEntityManager.HasComponent<LitMeshRenderData>(entity))
                    litIndex++;
            });
            NativeArray<BlobAssetReference<SimpleMeshData>> simpleblobs = new NativeArray<BlobAssetReference<SimpleMeshData>>(simpleIndex, Allocator.TempJob);
            NativeArray<BlobAssetReference<LitMeshData>> litblobs = new NativeArray<BlobAssetReference<LitMeshData>>(litIndex, Allocator.TempJob);

            simpleIndex = 0;
            litIndex = 0;

            // Check which blob assets to re-compute
            Entities.ForEach((UnityEngine.Mesh uMesh) =>
            {
                var hash = new Hash128((uint)uMesh.GetHashCode(), (uint)uMesh.vertexCount.GetHashCode(), (uint) uMesh.subMeshCount.GetHashCode(), 0);

                var entity = GetPrimaryEntity(uMesh);

                //Schedule blob asset recomputation jobs 
                if (DstEntityManager.HasComponent<SimpleMeshRenderData>(entity))
                {
                    simpleMeshContext.AssociateBlobAssetWithUnityObject(hash, uMesh);
                    if (simpleMeshContext.NeedToComputeBlobAsset(hash))
                    {
                        var singleMeshData = new UMeshDataCache();
                        singleMeshData.RetrieveSimpleMeshData(uMesh);

                        UMeshSettings uMeshSettings = new UMeshSettings(hash, uMesh, simpleIndex++);
                        simpleMeshContext.AddBlobAssetToCompute(hash, uMeshSettings);
                        var job = new SimpleMeshConversionJob(uMeshSettings, singleMeshData, simpleblobs);
                        combinedJH = JobHandle.CombineDependencies(combinedJH, job.Schedule(combinedJH));
                    }
                }
                if(DstEntityManager.HasComponent<LitMeshRenderData>(entity))
                {
                    litMeshContext.AssociateBlobAssetWithUnityObject(hash, uMesh);
                    if (litMeshContext.NeedToComputeBlobAsset(hash))
                    {
                        var litMeshData = new UMeshDataCache();
                        litMeshData.RetrieveLitMeshData(uMesh);
                        UMeshSettings uMeshSettings = new UMeshSettings(hash, uMesh, litIndex++);
                        litMeshContext.AddBlobAssetToCompute(hash, uMeshSettings);
                        var job = new LitMeshConversionJob(uMeshSettings, litMeshData, litblobs);
                        combinedJH = JobHandle.CombineDependencies(combinedJH, job.Schedule(combinedJH));
                    }
                }
            });

            // Re-compute the new blob assets
            combinedJH.Complete();

            // Update the BlobAssetStore
            using (var simpleMeshSettings = simpleMeshContext.GetSettings(Allocator.TempJob))
            {
                for (int i = 0; i < simpleMeshSettings.Length; i++)
                {
                    simpleMeshContext.AddComputedBlobAsset(simpleMeshSettings[i].hash, simpleblobs[simpleMeshSettings[i].blobIndex]);
                }
            }
            using (var litMeshSettings = litMeshContext.GetSettings(Allocator.TempJob))
            {
                for (int i = 0; i < litMeshSettings.Length; i++)
                {
                    litMeshContext.AddComputedBlobAsset(litMeshSettings[i].hash, litblobs[litMeshSettings[i].blobIndex]);
                }
            }

            // Use blob assets in the conversion
            Entities.ForEach((UnityEngine.Mesh uMesh) =>
            {
                var entity = GetPrimaryEntity(uMesh);
                bool addBounds = false;
                if (DstEntityManager.HasComponent<SimpleMeshRenderData>(entity))
                {
                    simpleMeshContext.GetBlobAsset(new Hash128((uint)uMesh.GetHashCode(), (uint)uMesh.vertexCount.GetHashCode(), (uint) uMesh.subMeshCount.GetHashCode(), 0), out var blob);
                    DstEntityManager.AddComponentData(entity, new SimpleMeshRenderData()
                    {
                        Mesh = blob
                    });
                    addBounds = true;
                }
                if (DstEntityManager.HasComponent<LitMeshRenderData>(entity))
                {
                    litMeshContext.GetBlobAsset(new Hash128((uint)uMesh.GetHashCode(), (uint)uMesh.vertexCount.GetHashCode(), (uint) uMesh.subMeshCount.GetHashCode(), 0), out var blob);
                    DstEntityManager.AddComponentData(entity, new LitMeshRenderData()
                    {
                        Mesh = blob
                    });
                    addBounds = true;
                }
                if (addBounds) 
                {
                    DstEntityManager.AddComponentData(entity, new MeshBounds {
                        Bounds = new AABB {
                            Center = uMesh.bounds.center,
                            Extents = uMesh.bounds.extents
                        }
                    });
                }
            });
            
            simpleMeshContext.Dispose();
            litMeshContext.Dispose();
            simpleblobs.Dispose();
            litblobs.Dispose();
        }
    }
}
