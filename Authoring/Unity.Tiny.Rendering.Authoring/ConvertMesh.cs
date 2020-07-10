using System;
using Unity.Entities;
using Unity.Tiny.Rendering;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;
using Unity.Jobs;
using System.Collections.Generic;
using Debug = Unity.Tiny.Debug;

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
        public NativeArray<Vector4> uBoneWeights;
        public NativeArray<Vector4> uBoneIndices;
        public NativeArray<Color> uColors;
        public NativeArray<ushort> uIndices;

        public unsafe void RetrieveSimpleMeshData(Mesh uMesh, int vertexCapacity = 0)
        {
            if (vertexCapacity == 0)
                vertexCapacity = uMesh.vertexCount;
            //Invert uvs
            var uvs = uMesh.uv;
            uUVs = new NativeArray<Vector2>(vertexCapacity, Allocator.TempJob);
            for (int i = 0; i < uvs.Length; i++)
            {
                uvs[i].y = 1 - uvs[i].y;
                uUVs[i] = uvs[i];
            }

            var vertices = uMesh.vertices;
            int vertexCount = vertices.Length;
            uPositions = new NativeArray<Vector3>(vertexCapacity, Allocator.TempJob);
            NativeArray<Vector3>.Copy(vertices, uPositions, vertexCount);

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

            uBoneWeights = new NativeArray<Vector4>(vertexCapacity, Allocator.TempJob);
            uBoneIndices = new NativeArray<Vector4>(vertexCapacity, Allocator.TempJob);
            bool hasBoneWeights = uMesh.boneWeights.Length > 0;
            BoneWeight[] boneWeights = uMesh.boneWeights;
            for (int i = 0; i < vertexCount; i++)
            {
                if (hasBoneWeights)
                {
                    BoneWeight boneWeight = boneWeights[i];
                    uBoneWeights[i] = new Vector4(boneWeight.weight0, boneWeight.weight1, boneWeight.weight2, boneWeight.weight3);
                    uBoneIndices[i] = new Vector4(boneWeight.boneIndex0, boneWeight.boneIndex1, boneWeight.boneIndex2, boneWeight.boneIndex3);
                }
                else
                {
                    uBoneWeights[i] = Vector4.zero;
                    uBoneIndices[i] = Vector4.zero;
                }
            }
        }

        public unsafe void RetrieveLitMeshData(Mesh uMesh, int vertexCapacity = 0)
        {
            if (vertexCapacity == 0)
                vertexCapacity = uMesh.vertexCount;

            RetrieveSimpleMeshData(uMesh, vertexCapacity);

            Vector4[] tang4 = uMesh.tangents; //uMesh.tangents is vector4 with x,y,z components, and w used to flip the binormal.
            Vector3[] nor = uMesh.normals;
            Vector3[] biTang = new Vector3[tang4.Length];

            if (tang4.Length != nor.Length)
                UnityEngine.Debug.LogWarning($"The mesh {uMesh.name} should have the same number of normals {nor.Length} and tangents {tang4.Length}");

            uNormals = new NativeArray<Vector3>(vertexCapacity, Allocator.TempJob);
            uTangents = new NativeArray<Vector3>(vertexCapacity, Allocator.TempJob);
            uBiTangents = new NativeArray<Vector3>(vertexCapacity, Allocator.TempJob);

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
            uColors = new NativeArray<Color>(vertexCapacity, Allocator.TempJob);
            NativeArray<Color>.Copy(colors, uColors, colors.Length);
        }

        private bool IsValidBoneIndex(float weight)
        {
            return math.abs(weight) > 0.00001f;
        }

        private int CalcToBeAddBoneIndexCount(Dictionary<int, int> boneIndexCounter, BoneWeight boneWeight)
        {
            int counter = 0;
            if (IsValidBoneIndex(boneWeight.weight0) && !boneIndexCounter.ContainsKey(boneWeight.boneIndex0))
                counter++;
            if (IsValidBoneIndex(boneWeight.weight1) && !boneIndexCounter.ContainsKey(boneWeight.boneIndex1))
                counter++;
            if (IsValidBoneIndex(boneWeight.weight2) && !boneIndexCounter.ContainsKey(boneWeight.boneIndex2))
                counter++;
            if (IsValidBoneIndex(boneWeight.weight3) && !boneIndexCounter.ContainsKey(boneWeight.boneIndex3))
                counter++;
            return counter;
        }

        private int GetNewBoneIndex(Dictionary<int, int> boneIndexCounter, float weight, int boneIndex)
        {
            if (!IsValidBoneIndex(weight))
                return 0;

            int newBoneIndex = 0;
            bool existing = boneIndexCounter.TryGetValue(boneIndex, out newBoneIndex);
            if (existing)
                return newBoneIndex;

            newBoneIndex = boneIndexCounter.Count;
            boneIndexCounter[boneIndex] = newBoneIndex;
            return newBoneIndex;
        }

        public void RetrieveLitSkinnedMeshData(Mesh uMesh, Entity meshEntity, EntityManager entityManager)
        {
            List<int> duplicateVertexIndex = new List<int>();
            List<Vector4> duplicateBoneIndex = new List<Vector4>();
            Dictionary<int, Vector4> existingVertex2NewBoneIndex = new Dictionary<int, Vector4>();
            Dictionary<int, int> boneIndexCounter = new Dictionary<int, int>();
            List<int> gpuDrawRange = new List<int>();

            BoneWeight[] boneWeights = uMesh.boneWeights;
            int[] triangles = uMesh.triangles;

            //Separate mesh into different draw range for gpu skinning use
            for (int subMeshIndex = 0; subMeshIndex < uMesh.subMeshCount; subMeshIndex++)
            {
                UnityEngine.Rendering.SubMeshDescriptor uSubMeshDescriptor = uMesh.GetSubMesh(subMeshIndex);
                int curIndex = uSubMeshDescriptor.indexStart;
                int lastIndex = uSubMeshDescriptor.indexStart;
                int endIndex = curIndex + uSubMeshDescriptor.indexCount;
                while (curIndex < endIndex)
                {
                    int curBoneCount = boneIndexCounter.Count;
                    for (int offset = 0; offset < 3; offset++)
                    {
                        int vertexIndex = triangles[curIndex + offset];
                        BoneWeight boneWeight = boneWeights[vertexIndex];
                        curBoneCount += CalcToBeAddBoneIndexCount(boneIndexCounter, boneWeight);
                    }

                    if (curBoneCount > MeshSkinningConfig.GPU_SKINNING_MAX_BONES)
                    {
                        gpuDrawRange.Add(curIndex);
                        Debug.Log("GPU SkinnedMesh Draw Range[" + lastIndex + ":" + curIndex + "] BoneCount:" + boneIndexCounter.Count);
                        lastIndex = curIndex;
                        boneIndexCounter.Clear();
                    }
                    else
                    {
                        for (int offset = 0; offset < 3; offset++)
                        {
                            int vertexIndex = triangles[curIndex + offset];
                            BoneWeight curBoneWeight = boneWeights[vertexIndex];

                            //restore the new bone index and set it to the mesh later
                            Vector4 newBoneIndex = new Vector4();
                            newBoneIndex.x = GetNewBoneIndex(boneIndexCounter, curBoneWeight.weight0, curBoneWeight.boneIndex0);
                            newBoneIndex.y = GetNewBoneIndex(boneIndexCounter, curBoneWeight.weight1, curBoneWeight.boneIndex1);
                            newBoneIndex.z = GetNewBoneIndex(boneIndexCounter, curBoneWeight.weight2, curBoneWeight.boneIndex2);
                            newBoneIndex.w = GetNewBoneIndex(boneIndexCounter, curBoneWeight.weight3, curBoneWeight.boneIndex3);

                            Vector4 existingNewBoneIndex = new Vector4();
                            bool isExist = existingVertex2NewBoneIndex.TryGetValue(vertexIndex, out existingNewBoneIndex);
                            if (isExist && newBoneIndex != existingNewBoneIndex)
                            {
                                bool needAdd = true;
                                int newVertexIndex = 0;
                                for (int j = 0; j < duplicateVertexIndex.Count; j++)
                                {
                                    if (duplicateVertexIndex[j] == vertexIndex && duplicateBoneIndex[j] == newBoneIndex)
                                    {
                                        newVertexIndex = uMesh.vertexCount + j;
                                        triangles[curIndex + offset] = newVertexIndex;
                                        needAdd = false;
                                        break;
                                    }
                                }

                                if (needAdd)
                                {
                                    duplicateVertexIndex.Add(vertexIndex);
                                    duplicateBoneIndex.Add(newBoneIndex);
                                    newVertexIndex =  uMesh.vertexCount + duplicateVertexIndex.Count - 1;
                                    triangles[curIndex + offset] = newVertexIndex;
                                    existingVertex2NewBoneIndex[newVertexIndex] = newBoneIndex;
                                }
                            }
                            else
                            {
                                existingVertex2NewBoneIndex[vertexIndex] = newBoneIndex;
                            }
                        }

                        curIndex += 3;
                    }
                }

                if (lastIndex != curIndex)
                {
                    gpuDrawRange.Add(curIndex);
                    Debug.Log("GPU SkinnedMesh Draw Range[" + lastIndex + ":" + curIndex + "] BoneCount:" + boneIndexCounter.Count);
                }
            }
            Debug.Log("GPU SkinnedMesh Duplicate VertexCount:" + duplicateVertexIndex.Count);
            Debug.Log("GPU SkinnedMesh DrawCalls: " + gpuDrawRange.Count);

            //generate UMeshDataCache and adding duplicate vertices into UMeshDataCache
            int newVertexCount = uMesh.vertexCount + duplicateVertexIndex.Count;
            RetrieveLitMeshData(uMesh, newVertexCount);
            for (int i = 0; i < duplicateVertexIndex.Count; i++)
            {
                int curVertexIndex = uMesh.vertexCount + i;
                int originalVertexIndex = duplicateVertexIndex[i];
                uPositions[curVertexIndex] = uPositions[originalVertexIndex];
                uUVs[curVertexIndex] = uUVs[originalVertexIndex];
                uNormals[curVertexIndex] = uNormals[originalVertexIndex];
                uTangents[curVertexIndex] = uTangents[originalVertexIndex];
                uBiTangents[curVertexIndex] = uBiTangents[originalVertexIndex];
                uBoneWeights[curVertexIndex] = uBoneWeights[originalVertexIndex];
                uBoneIndices[curVertexIndex] = uBoneIndices[originalVertexIndex];
                uColors[curVertexIndex] = uColors[originalVertexIndex];
            }
            //Update the indices, some of the triangles reference to the duplicate vertex
            for (int i = 0; i < triangles.Length; i++)
            {
                uIndices[i] = Convert.ToUInt16(triangles[i]);
            }
            //Restore the original vertex bone index for switching GPU skinning to CPU skinning in the runtime
            DynamicBuffer<OriginalVertexBoneIndex> obiBuffer = entityManager.AddBuffer<OriginalVertexBoneIndex>(meshEntity);
            for (int i = 0; i < newVertexCount; i++)
            {
                Vector4 uBoneIndex = uBoneIndices[i];
                obiBuffer.Add(new OriginalVertexBoneIndex { BoneIndex = new float4(uBoneIndex.x, uBoneIndex.y, uBoneIndex.z, uBoneIndex.w)});
                Vector4 newBoneIndex = existingVertex2NewBoneIndex[i];
                uBoneIndices[i] = newBoneIndex;
            }
            //Add GPUSkinnedMeshDrawRange for SkinnedMeshRendererConversion use.
            DynamicBuffer<GPUSkinnedMeshDrawRange> gsmdrBuffer = entityManager.AddBuffer<GPUSkinnedMeshDrawRange>(meshEntity);
            for (int i = 0; i < gpuDrawRange.Count; i++)
            {
                gsmdrBuffer.Add(new GPUSkinnedMeshDrawRange { TriangleIndex =  gpuDrawRange[i]});
            }
        }
    }

    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.DotsRuntimeGameObjectConversion)]
    public class MeshConversion : GameObjectConversionSystem
    {
        void CheckForMeshLimitations(Mesh uMesh)
        {
            int vertexCount = uMesh.vertexCount;
            if (vertexCount > UInt16.MaxValue)
                throw new ArgumentException($"The maximum number of vertices supported per mesh is {UInt16.MaxValue} and the mesh {uMesh.name} has {vertexCount} vertices. Please use a lighter mesh instead.");
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
                CheckForMeshLimitations(uMesh);
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
                var hash = new Hash128((uint)uMesh.GetHashCode(), (uint)uMesh.vertexCount.GetHashCode(), (uint)uMesh.subMeshCount.GetHashCode(), 0);

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
                if (DstEntityManager.HasComponent<LitMeshRenderData>(entity))
                {
                    litMeshContext.AssociateBlobAssetWithUnityObject(hash, uMesh);
                    if (litMeshContext.NeedToComputeBlobAsset(hash))
                    {
                        var litMeshData = new UMeshDataCache();
                        if (DstEntityManager.HasComponent<NeedGenerateGPUSkinnedMeshRenderer>(entity))
                            litMeshData.RetrieveLitSkinnedMeshData(uMesh, entity, DstEntityManager);
                        else
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
                    simpleMeshContext.GetBlobAsset(new Hash128((uint)uMesh.GetHashCode(), (uint)uMesh.vertexCount.GetHashCode(), (uint)uMesh.subMeshCount.GetHashCode(), 0), out var blob);
                    DstEntityManager.AddComponentData(entity, new SimpleMeshRenderData()
                    {
                        Mesh = blob
                    });
                    addBounds = true;
                }
                if (DstEntityManager.HasComponent<LitMeshRenderData>(entity))
                {
                    litMeshContext.GetBlobAsset(new Hash128((uint)uMesh.GetHashCode(), (uint)uMesh.vertexCount.GetHashCode(), (uint)uMesh.subMeshCount.GetHashCode(), 0), out var blob);
                    DstEntityManager.AddComponentData(entity, new LitMeshRenderData()
                    {
                        Mesh = blob
                    });
                    addBounds = true;
                }
                if (addBounds)
                {
                    DstEntityManager.AddComponentData(entity, new MeshBounds
                    {
                        Bounds = new AABB
                        {
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

    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    [UpdateAfter(typeof(SkinnedMeshRendererConversion))]
    [WorldSystemFilter(WorldSystemFilterFlags.DotsRuntimeGameObjectConversion)]
    public class CleanupAfterConvertSkinnedMeshSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.Mesh uMesh) =>
            {
                Entity meshEntity = GetPrimaryEntity(uMesh);
                if (DstEntityManager.HasComponent<NeedGenerateGPUSkinnedMeshRenderer>(meshEntity))
                {
                    DstEntityManager.RemoveComponent<NeedGenerateGPUSkinnedMeshRenderer>(meshEntity);
                }

                if (DstEntityManager.HasComponent<GPUSkinnedMeshDrawRange>(meshEntity))
                {
                    DstEntityManager.RemoveComponent<GPUSkinnedMeshDrawRange>(meshEntity);
                }
            });
        }
    }
}
