using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Unity.Tiny.Rendering;
using Unity.Entities.Runtime.Build;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Debug = Unity.Tiny.Debug;
using SkinnedMeshRenderer = UnityEngine.SkinnedMeshRenderer;
using SkinQuality = Unity.Tiny.Rendering.SkinQuality;

namespace Unity.TinyConversion
{
    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.DotsRuntimeGameObjectConversion)]
    public class SkinnedMeshRendererDeclareAssets : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.SkinnedMeshRenderer uSkinnedMeshRenderer) =>
            {
                foreach (Material mat in uSkinnedMeshRenderer.sharedMaterials)
                {
                    DeclareReferencedAsset(mat);
                    DeclareAssetDependency(uSkinnedMeshRenderer.gameObject, mat);

                    int[] ids = mat.GetTexturePropertyNameIDs();
                    for (int i = 0; i < ids.Length; i++)
                    {
                        var texture = mat.GetTexture(ids[i]);
                        if( texture != null)
                            DeclareAssetDependency(uSkinnedMeshRenderer.gameObject, texture);
                    }
                }

                if (uSkinnedMeshRenderer.sharedMesh == null)
                    UnityEngine.Debug.LogWarning("Missing mesh in SkinnedMeshRenderer on gameobject: " +
                                                 uSkinnedMeshRenderer.gameObject.name);

                DeclareReferencedAsset(uSkinnedMeshRenderer.sharedMesh);
                DeclareAssetDependency(uSkinnedMeshRenderer.gameObject, uSkinnedMeshRenderer.sharedMesh);
            });
        }
    }

    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    [UpdateBefore(typeof(MeshConversion))]
    [UpdateAfter(typeof(MaterialConversion))]
    [WorldSystemFilter(WorldSystemFilterFlags.DotsRuntimeGameObjectConversion)]
    public class MarkMeshAsGPUSkinnedMeshSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.SkinnedMeshRenderer uSkinnedMeshRenderer) =>
            {
                var sharedMaterials = uSkinnedMeshRenderer.sharedMaterials;
                UnityEngine.Mesh uMesh = uSkinnedMeshRenderer.sharedMesh;
                var meshEntity = GetPrimaryEntity(uMesh);

                for (int i = 0; i < uMesh.subMeshCount; i++)
                {
                    // Find the target material entity to be used for this submesh
                    Entity targetMaterial = MeshRendererConversion.FindTargetMaterialEntity(this, sharedMaterials, i);

                    var isLit = DstEntityManager.HasComponent<LitMaterial>(targetMaterial);
                    var isSimple = DstEntityManager.HasComponent<SimpleMaterial>(targetMaterial);
                    if (isSimple)
                    {
                        Debug.Log("Unlit material was not supported in SkinnedMeshRenderer:" + uMesh.name);
                        continue;
                    }

                    if (isLit)
                    {
                        if (uSkinnedMeshRenderer.bones.Length > MeshSkinningConfig.GPU_SKINNING_MAX_BONES)
                            DstEntityManager.AddComponent<NeedGenerateGPUSkinnedMeshRenderer>(meshEntity);
                        DstEntityManager.AddComponent<LitMeshRenderData>(meshEntity);
                        // Remove simple data if it was there, we don't need it
                        DstEntityManager.RemoveComponent<SimpleMeshRenderData>(meshEntity);
                    }
                }
            });
        }
    }

    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    [UpdateAfter(typeof(MeshConversion))]
    [UpdateAfter(typeof(MaterialConversion))]
    [WorldSystemFilter(WorldSystemFilterFlags.DotsRuntimeGameObjectConversion)]
    public class SkinnedMeshRendererConversion : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.SkinnedMeshRenderer uSkinnedMeshRenderer) =>
            {
                var sharedMaterials = uSkinnedMeshRenderer.sharedMaterials;
                UnityEngine.Mesh uMesh = uSkinnedMeshRenderer.sharedMesh;
                var meshEntity = GetPrimaryEntity(uMesh);

                List<Entity> subSkinnedMeshRenderers = new List<Entity>();
                for (int i = 0; i < uMesh.subMeshCount; i++)
                {
                    // Find the target material entity to be used for this submesh
                    Entity targetMaterial = MeshRendererConversion.FindTargetMaterialEntity(this, sharedMaterials, i);

                    var isLit = DstEntityManager.HasComponent<LitMaterial>(targetMaterial);
                    var isSimple = DstEntityManager.HasComponent<SimpleMaterial>(targetMaterial);
                    if (isSimple)
                    {
                        Debug.Log("Unlit material was not supported in SkinnedMeshRenderer:" + uMesh.name);
                        continue;
                    }

                    if (isLit)
                    {
                        Entity subSkinnedMeshRendererEntity =
                            ConvertOriginalSubMesh(this, uSkinnedMeshRenderer, uMesh, meshEntity, i, targetMaterial);
                        subSkinnedMeshRenderers.Add(subSkinnedMeshRendererEntity);
                    }
                }

                List<Entity> ret = ConvertGPUSkinnedSubMesh(this, uSkinnedMeshRenderer, uMesh, meshEntity);
                if (ret != null)
                    subSkinnedMeshRenderers.AddRange(ret);
                for (int j = 0; j < subSkinnedMeshRenderers.Count; j++)
                {
                    Entity subSkinnedMeshRenderer = subSkinnedMeshRenderers[j];
                    DstEntityManager.AddComponent<LitMeshRenderer>(subSkinnedMeshRenderer);
                }

                ConvertSkinnedMeshBoneInfoToTransformEntity(this, uSkinnedMeshRenderer);
            });
        }

        private static Entity GenerateMeshRendererEntity(GameObjectConversionSystem gsys,
            UnityEngine.SkinnedMeshRenderer uSkinnedMeshRenderer, Entity meshEntity, Entity materialEntity, int startIndex,
            int indexCount, bool createAdditionlEntity, bool canUseGPUSkinning, bool canUseCPUSkinning)
        {
            Entity primarySkinnedMeshRenderer = gsys.GetPrimaryEntity(uSkinnedMeshRenderer);
            Entity meshRendererEntity = primarySkinnedMeshRenderer;

            if (createAdditionlEntity)
            {
                meshRendererEntity = gsys.CreateAdditionalEntity(uSkinnedMeshRenderer);
                MeshRendererConversion.AddTransformComponent(gsys, primarySkinnedMeshRenderer, meshRendererEntity);
            }

            Unity.Tiny.Rendering.SkinnedMeshRenderer smr = new Unity.Tiny.Rendering.SkinnedMeshRenderer();
            smr.sharedMesh = meshEntity;
            smr.material = materialEntity;
            smr.startIndex = startIndex;
            smr.indexCount = indexCount;
            smr.canUseGPUSkinning = canUseGPUSkinning;
            smr.canUseCPUSkinning = canUseCPUSkinning;
            smr.shadowCastingMode = (Unity.Tiny.Rendering.ShadowCastingMode) uSkinnedMeshRenderer.shadowCastingMode;
            smr.skinQuality = ConvertSkinQuality(uSkinnedMeshRenderer);
            gsys.DstEntityManager.AddComponentData(meshRendererEntity, smr);

            gsys.DstEntityManager.AddComponentData(meshRendererEntity, new WorldBounds());
            return meshRendererEntity;
        }

        private static SkinQuality ConvertSkinQuality(SkinnedMeshRenderer uSkinnedMeshRenderer)
        {
            int boneCount = (int) uSkinnedMeshRenderer.quality;
            if (uSkinnedMeshRenderer.quality == UnityEngine.SkinQuality.Auto)
                boneCount = (int) QualitySettings.skinWeights;

            if (boneCount > (int) SkinQuality.Bone4)
                return SkinQuality.Bone4;

            return (SkinQuality) boneCount;
        }

        private static Entity ConvertOriginalSubMesh(GameObjectConversionSystem gsys,
            UnityEngine.SkinnedMeshRenderer uSkinnedMeshRenderer, UnityEngine.Mesh uMesh, Entity meshEntity, int subMeshIndex,
            Entity materialEntity)
        {
            int boneCount = uSkinnedMeshRenderer.bones.Length;
            bool canUseCPUSkinning = boneCount > 0;
            bool canUseGPUSkinning = boneCount > 0 && boneCount <= MeshSkinningConfig.GPU_SKINNING_MAX_BONES;

            int startIndex = Convert.ToUInt16(uMesh.GetIndexStart(subMeshIndex));
            int indexCount = Convert.ToUInt16(uMesh.GetIndexCount(subMeshIndex));
            Entity meshRendererEntity = GenerateMeshRendererEntity(gsys, uSkinnedMeshRenderer, meshEntity, materialEntity,
                startIndex, indexCount, subMeshIndex > 0, canUseGPUSkinning, canUseCPUSkinning);

            DynamicBuffer<SkinnedMeshBoneRef>
                smbrBuffer = gsys.DstEntityManager.AddBuffer<SkinnedMeshBoneRef>(meshRendererEntity);
            for (int i = 0; i < boneCount; i++)
            {
                smbrBuffer.Add(new SkinnedMeshBoneRef
                {
                    bone = gsys.GetPrimaryEntity(uSkinnedMeshRenderer.bones[i]),
                });
            }

            return meshRendererEntity;
        }

        private static List<Entity> ConvertGPUSkinnedSubMesh(GameObjectConversionSystem gsys,
            UnityEngine.SkinnedMeshRenderer uSkinnedMeshRenderer, UnityEngine.Mesh uMesh, Entity meshEntity)
        {
            List<Entity> entities = new List<Entity>();
            bool needGPUSkinningData = gsys.DstEntityManager.HasComponent<GPUSkinnedMeshDrawRange>(meshEntity);
            if (!needGPUSkinningData)
                return null;

            //Get the gpu draw range And calc the bone reference for the draw range
            LitMeshRenderData litMeshRenderData = gsys.DstEntityManager.GetComponentData<LitMeshRenderData>(meshEntity);
            ref LitMeshData litMeshData = ref litMeshRenderData.Mesh.Value;
            DynamicBuffer<GPUSkinnedMeshDrawRange> gsmdrBuffer =
                gsys.DstEntityManager.GetBuffer<GPUSkinnedMeshDrawRange>(meshEntity);
            DynamicBuffer<OriginalVertexBoneIndex> ovbiBuffer =
                gsys.DstEntityManager.GetBuffer<OriginalVertexBoneIndex>(meshEntity);
            List<int> drawRange = new List<int>();
            List<int[]> boneRefsList = new List<int[]>();
            int startIndex = 0;
            for (int i = 0; i < gsmdrBuffer.Length; i++)
            {
                int endIndex = gsmdrBuffer[i].TriangleIndex;
                drawRange.Add(endIndex);

                int[] boneRefs = new int[MeshSkinningConfig.GPU_SKINNING_MAX_BONES];
                for (int j = 0; j < MeshSkinningConfig.GPU_SKINNING_MAX_BONES; j++)
                    boneRefs[j] = 0;

                for (int index = startIndex; index < endIndex; index++)
                {
                    int vertexIndex = litMeshData.Indices[index];
                    LitVertex litVertex = litMeshData.Vertices[vertexIndex];
                    OriginalVertexBoneIndex originalVertexBoneIndex = ovbiBuffer[vertexIndex];
                    if (math.abs(litVertex.BoneWeight.x) > float.Epsilon)
                        boneRefs[(int) litVertex.BoneIndex.x] = (int) originalVertexBoneIndex.BoneIndex.x;
                    if (math.abs(litVertex.BoneWeight.y) > float.Epsilon)
                        boneRefs[(int) litVertex.BoneIndex.y] = (int) originalVertexBoneIndex.BoneIndex.y;
                    if (math.abs(litVertex.BoneWeight.z) > float.Epsilon)
                        boneRefs[(int) litVertex.BoneIndex.z] = (int) originalVertexBoneIndex.BoneIndex.z;
                    if (math.abs(litVertex.BoneWeight.w) > float.Epsilon)
                        boneRefs[(int) litVertex.BoneIndex.w] = (int) originalVertexBoneIndex.BoneIndex.w;
                }

                boneRefsList.Add(boneRefs);
            }

            //Generate entities for draw range and setup bone reference
            startIndex = 0;
            int curSubMeshIndex = 0;
            var sharedMaterials = uSkinnedMeshRenderer.sharedMaterials;
            for (int i = 0; i < drawRange.Count; i++)
            {
                int endIndex = drawRange[i];
                while (true)
                {
                    SubMeshDescriptor subMeshDescriptor = uMesh.GetSubMesh(curSubMeshIndex);
                    if (endIndex <= (subMeshDescriptor.indexStart + subMeshDescriptor.indexCount))
                        break;
                    curSubMeshIndex++;
                }

                int indexCount = endIndex - startIndex;
                Entity materialEntity = MeshRendererConversion.FindTargetMaterialEntity(gsys, sharedMaterials, i);
                Entity meshRendererEntity = GenerateMeshRendererEntity(gsys, uSkinnedMeshRenderer, meshEntity, materialEntity,
                    startIndex, indexCount, true, true, false);

                gsys.DstEntityManager.AddBuffer<SkinnedMeshBoneRef>(meshRendererEntity);
                int[] boneRefs = boneRefsList[i];
                for (int j = 0; j < boneRefs.Length; j++)
                {
                    gsys.DstEntityManager.GetBuffer<SkinnedMeshBoneRef>(meshRendererEntity).Add(new SkinnedMeshBoneRef
                    {
                        bone = gsys.GetPrimaryEntity(uSkinnedMeshRenderer.bones[boneRefs[j]]),
                    });
                }

                startIndex = endIndex;
                entities.Add(meshRendererEntity);
            }

            return entities;
        }

        private static void ConvertSkinnedMeshBoneInfoToTransformEntity(GameObjectConversionSystem gsys,
            UnityEngine.SkinnedMeshRenderer uSkinnedMeshRenderer)
        {
            UnityEngine.Mesh mesh = uSkinnedMeshRenderer.sharedMesh;
            Entity skinnedMeshRendererEntity = gsys.GetPrimaryEntity(uSkinnedMeshRenderer);
            for (int i = 0; i < uSkinnedMeshRenderer.bones.Length; i++)
            {
                Entity transformEntity = gsys.GetPrimaryEntity(uSkinnedMeshRenderer.bones[i]);
                gsys.DstEntityManager.AddComponentData(transformEntity, new SkinnedMeshBoneInfo
                {
                    smrEntity = skinnedMeshRendererEntity,
                    bindpose = mesh.bindposes[i],
                });
            }
        }
    }
}
