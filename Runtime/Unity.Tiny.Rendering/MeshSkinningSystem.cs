using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Unity.Tiny.Rendering
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public unsafe class ConvertLitMeshToDynamicMeshSystem : SystemBase
    {
        protected void ConvertLitMeshRenderDataToDynamicMeshData(EntityCommandBuffer ecb, Entity smrEntity, SkinnedMeshRenderer smr)
        {
            if (smr.dynamicMesh != Entity.Null)
                return;

            Entity meshEntity = smr.sharedMesh;
            LitMeshRenderData litMeshRenderData = EntityManager.GetComponentData<LitMeshRenderData>(meshEntity);
            ref LitMeshData litMeshData = ref litMeshRenderData.Mesh.Value;
            int indicesCount = litMeshData.Indices.Length;
            int verticesCount = litMeshData.Vertices.Length;

            Entity dynamicMeshEntity = ecb.CreateEntity();
            smr.dynamicMesh = dynamicMeshEntity;
            ecb.SetComponent(smrEntity, smr);

            //start to convert mesh data to dynamic mesh data, add it into SkinnedMeshRenderer entity
            DynamicMeshData dmd = new DynamicMeshData
            {
                Dirty = true,
                IndexCapacity = indicesCount,
                VertexCapacity = verticesCount,
                NumIndices = indicesCount,
                NumVertices = verticesCount,
                UseDynamicGPUBuffer = true
            };
            ecb.AddComponent(dynamicMeshEntity, dmd);

            DynamicBuffer<DynamicLitVertex> dlvBuffer = ecb.AddBuffer<DynamicLitVertex>(dynamicMeshEntity);
            dlvBuffer.ResizeUninitialized(verticesCount);
            void* verticesPtr = litMeshData.Vertices.GetUnsafePtr();
            byte* dlvBufferPtr = (byte*) dlvBuffer.GetUnsafePtr();
            int litVertexSize = UnsafeUtility.SizeOf<LitVertex>();
            UnsafeUtility.MemCpy(dlvBufferPtr, verticesPtr, verticesCount * litVertexSize);

            //if needRevertBoneIndex is true, means that the bone index in mesh blob data was re-order by converter
            bool needRevertBoneIndex = EntityManager.HasComponent<OriginalVertexBoneIndex>(meshEntity);
            if (needRevertBoneIndex && smr.canUseCPUSkinning)
            {
                DynamicBuffer<OriginalVertexBoneIndex> ovbiBuffer =
                    EntityManager.GetBuffer<OriginalVertexBoneIndex>(meshEntity);
                void* ovbiBufferPtr = ovbiBuffer.GetUnsafePtr();
                int ovbiSize = UnsafeUtility.SizeOf<OriginalVertexBoneIndex>();
                int offset = sizeof(float3) * 3 + sizeof(float2) + sizeof(float4); //Position,TexCoord0,Normal,Tangent,BoneWeight
                UnsafeUtility.MemCpyStride(dlvBufferPtr + offset, litVertexSize, ovbiBufferPtr, ovbiSize,
                    ovbiSize, ovbiBuffer.Length);
            }

            DynamicBuffer<DynamicIndex> diBuffer = ecb.AddBuffer<DynamicIndex>(dynamicMeshEntity);
            diBuffer.ResizeUninitialized(indicesCount);
            void* indicesPtr = litMeshData.Indices.GetUnsafePtr();
            void* dlBufferPtr = diBuffer.GetUnsafePtr();
            UnsafeUtility.MemCpy(dlBufferPtr, indicesPtr, indicesCount * UnsafeUtility.SizeOf<ushort>());

            DynamicBuffer<OriginalVertex> ovBuffer = ecb.AddBuffer<OriginalVertex>(dynamicMeshEntity);
            ovBuffer.ResizeUninitialized(verticesCount);
            byte* ovBufferPtr = (byte*) ovBuffer.GetUnsafePtr();
            int originalVertexSize = UnsafeUtility.SizeOf<OriginalVertex>();
            //copy Position
            UnsafeUtility.MemCpyStride(ovBufferPtr, originalVertexSize, verticesPtr, litVertexSize, sizeof(float3),
                verticesCount);
            byte* offsetVerticesPtr = (byte*) verticesPtr + sizeof(float3) + sizeof(float2);
            //copy Normal and tangent
            UnsafeUtility.MemCpyStride(ovBufferPtr + sizeof(float3), originalVertexSize, offsetVerticesPtr,
                litVertexSize, 2 * sizeof(float3), verticesCount);
        }

        protected void DeleteDynamicMeshData(EntityCommandBuffer ecb, Entity smrEntity, SkinnedMeshRenderer smr)
        {
            if (smr.dynamicMesh == Entity.Null)
                return;

            ecb.DestroyEntity(smr.dynamicMesh);
            smr.dynamicMesh = Entity.Null;
            ecb.SetComponent(smrEntity, smr);
        }

        //if bonecount <= MeshSkinningConfig.GPU_SKINNING_MAX_BONES, then one SkinnedMeshRenderer may split to
        //type        canUseCPUSkinning   canUseGPUSkinning
        //original    true                true

        //if bonecount > MeshSkinningConfig.GPU_SKINNING_MAX_BONES, then one SkinnedMeshRenderer may split to servals
        //type        canUseCPUSkinning   canUseGPUSkinning
        //original    true                false
        //additional  false               true
        //additional  false               true
        //additional  false               true

        protected override void OnUpdate()
        {
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            DisplayInfo di = GetSingleton<DisplayInfo>();
            Entities.ForEach((Entity e, ref SkinnedMeshRenderer smr) =>
            {
                bool hasBlendShape = false;
                if (di.gpuSkinning)
                {
                    if (smr.canUseCPUSkinning && !hasBlendShape)
                        DeleteDynamicMeshData(ecb, e, smr);

                    if (smr.canUseGPUSkinning && hasBlendShape)
                        ConvertLitMeshRenderDataToDynamicMeshData(ecb, e, smr);
                }
                else
                {
                    if (smr.canUseCPUSkinning)
                        ConvertLitMeshRenderDataToDynamicMeshData(ecb, e, smr);

                    if (smr.canUseGPUSkinning)
                        DeleteDynamicMeshData(ecb, e, smr);
                }
            }).WithoutBurst().Run();
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ConvertLitMeshToDynamicMeshSystem))]
    public class CalcMeshBoneMatrixSystem : SystemBase
    {
        [BurstCompile]
        unsafe struct CalcMeshBoneMatrixJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorldType;
            public ComponentTypeHandle<SkinnedMeshBoneInfo> SkinnedMeshBoneInfoType;
            [ReadOnly] public ComponentDataFromEntity<LocalToWorld> ComponentDataFromEntityLocalToWorld;

            public unsafe void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                NativeArray<SkinnedMeshBoneInfo> chunkSkinnedMeshBoneInfo = chunk.GetNativeArray(SkinnedMeshBoneInfoType);
                NativeArray<LocalToWorld> chunkLocalToWorld = chunk.GetNativeArray(LocalToWorldType);

                int chunkCount = chunk.Count;
                for (int entityIndex = 0; entityIndex < chunkCount; entityIndex++)
                {
                    SkinnedMeshBoneInfo smbi = chunkSkinnedMeshBoneInfo[entityIndex];
                    float4x4 smrLocalToWorld = ComponentDataFromEntityLocalToWorld[smbi.smrEntity].Value;
                    float4x4 smrWorldToLocal = math.inverse(smrLocalToWorld);
                    float4x4 boneLocalToWorld = chunkLocalToWorld[entityIndex].Value;
                    smbi.bonematrix = math.mul(smrWorldToLocal, boneLocalToWorld);
                    smbi.bonematrix = math.mul(smbi.bonematrix, smbi.bindpose);
                    chunkSkinnedMeshBoneInfo[entityIndex] = smbi;
                }
            }
        }

        protected override void OnUpdate()
        {
            CalcMeshBoneMatrixJob calcJob = new CalcMeshBoneMatrixJob()
            {
                LocalToWorldType = GetComponentTypeHandle<LocalToWorld>(true),
                SkinnedMeshBoneInfoType = GetComponentTypeHandle<SkinnedMeshBoneInfo>(),
                ComponentDataFromEntityLocalToWorld = GetComponentDataFromEntity<LocalToWorld>(),
            };

            this.Dependency = calcJob.Schedule(m_query, this.Dependency);
        }

        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(
                ComponentType.ReadWrite<SkinnedMeshBoneInfo>()
            );
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CalcMeshBoneMatrixSystem))]
    public unsafe class CPUMeshSkinningSystem : SystemBase
    {
        [BurstCompile]
        unsafe struct CPUMeshSkinningJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<SkinnedMeshBoneRef> SkinnedMeshBoneRefType;
            [ReadOnly] public ComponentTypeHandle<SkinnedMeshRenderer> SkinnedMeshRendererType;

            [NativeDisableContainerSafetyRestriction]
            public BufferFromEntity<DynamicLitVertex> BufferDynamicLitVertex;

            [ReadOnly] public BufferFromEntity<OriginalVertex> BufferOriginalVertex;

            [NativeDisableContainerSafetyRestriction]
            public ComponentDataFromEntity<DynamicMeshData> ComponentDynamicMeshData;

            [ReadOnly] public ComponentDataFromEntity<SkinnedMeshBoneInfo> ComponentSkinnedMeshBoneInfo;

            private float4x4 GetBoneMatrix(DynamicBuffer<SkinnedMeshBoneRef> smbrBuffer, int boneIndex)
            {
                SkinnedMeshBoneRef boneRef = smbrBuffer[boneIndex];
                SkinnedMeshBoneInfo boneInfo = ComponentSkinnedMeshBoneInfo[boneRef.bone];
                return boneInfo.bonematrix;
            }

            public unsafe void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                BufferAccessor<SkinnedMeshBoneRef> smbrBufferAccessor = chunk.GetBufferAccessor(SkinnedMeshBoneRefType);
                NativeArray<SkinnedMeshRenderer> chunkSkinnedMeshRenderer = chunk.GetNativeArray(SkinnedMeshRendererType);
                for (int j = 0; j < chunk.Count; j++)
                {
                    SkinnedMeshRenderer skinnedMeshRenderer = chunkSkinnedMeshRenderer[j];
                    if (!skinnedMeshRenderer.canUseCPUSkinning || skinnedMeshRenderer.dynamicMesh == Entity.Null)
                        continue;

                    DynamicBuffer<SkinnedMeshBoneRef> smbrBuffer = smbrBufferAccessor[j];
                    DynamicBuffer<DynamicLitVertex> dlvBuffer = BufferDynamicLitVertex[skinnedMeshRenderer.dynamicMesh];
                    DynamicBuffer<OriginalVertex> ovBuffer = BufferOriginalVertex[skinnedMeshRenderer.dynamicMesh];
                    int vertexCount = ovBuffer.Length;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        DynamicLitVertex dynamicLitVertex = dlvBuffer[i];
                        float4 boneWeight = dynamicLitVertex.Value.BoneWeight;
                        float4 boneIndex = dynamicLitVertex.Value.BoneIndex;
                        OriginalVertex retVertex = ovBuffer[i];

                        float4x4 mat = float4x4.zero;
                        switch (skinnedMeshRenderer.skinQuality)
                        {
                            case SkinQuality.Bone1:
                                mat = GetBoneMatrix(smbrBuffer, (int) boneIndex.x);
                                break;
                            case SkinQuality.Bone2:
                                float4x4 boneMatrix = GetBoneMatrix(smbrBuffer, (int) boneIndex.x);
                                float invSum = 1 / (boneWeight.x + boneWeight.y);
                                float4x4 matX = boneWeight.x * invSum * boneMatrix;
                                boneMatrix = GetBoneMatrix(smbrBuffer, (int) boneIndex.y);
                                float4x4 matY = boneWeight.y * invSum * boneMatrix;
                                mat = matX + matY;
                                break;
                            case SkinQuality.Bone4:
                                boneMatrix = GetBoneMatrix(smbrBuffer, (int) boneIndex.x);
                                matX = boneWeight.x * boneMatrix;
                                boneMatrix = GetBoneMatrix(smbrBuffer, (int) boneIndex.y);
                                matY = boneWeight.y * boneMatrix;
                                boneMatrix = GetBoneMatrix(smbrBuffer, (int) boneIndex.z);
                                float4x4 matZ = boneWeight.z * boneMatrix;
                                boneMatrix = GetBoneMatrix(smbrBuffer, (int) boneIndex.w);
                                float4x4 matW = boneWeight.w * boneMatrix;
                                mat = matX + matY + matZ + matW;
                                break;
                        }
                        float4 retPosition = math.mul(mat, new float4(retVertex.Position, 1));
                        float4 retNormal = math.mul(mat, new float4(retVertex.Normal, 1));

                        dynamicLitVertex.Value.Position = retPosition.xyz;
                        dynamicLitVertex.Value.Normal = retNormal.xyz;
                        dlvBuffer[i] = dynamicLitVertex;
                    }

                    DynamicMeshData dmd = ComponentDynamicMeshData[skinnedMeshRenderer.dynamicMesh];
                    dmd.Dirty = true;
                    ComponentDynamicMeshData[skinnedMeshRenderer.dynamicMesh] = dmd;
                }
            }
        }

        protected override void OnUpdate()
        {
            DisplayInfo di = GetSingleton<DisplayInfo>();
            if (di.gpuSkinning)
                return;

            CPUMeshSkinningJob skinningJob = new CPUMeshSkinningJob()
            {
                SkinnedMeshBoneRefType = GetBufferTypeHandle<SkinnedMeshBoneRef>(true),
                SkinnedMeshRendererType = GetComponentTypeHandle<SkinnedMeshRenderer>(true),
                BufferDynamicLitVertex = GetBufferFromEntity<DynamicLitVertex>(),
                BufferOriginalVertex = GetBufferFromEntity<OriginalVertex>(true),
                ComponentDynamicMeshData = GetComponentDataFromEntity<DynamicMeshData>(),
                ComponentSkinnedMeshBoneInfo = GetComponentDataFromEntity<SkinnedMeshBoneInfo>(true)
            };

            this.Dependency = skinningJob.Schedule(m_query, this.Dependency);
        }

        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(
                ComponentType.ReadOnly<SkinnedMeshBoneRef>(),
                ComponentType.ReadOnly<SkinnedMeshRenderer>()
            );
        }
    }
}
