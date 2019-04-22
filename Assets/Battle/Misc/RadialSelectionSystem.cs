﻿//using Unity.Burst;
//using Unity.Collections;
//using Unity.Entities;
//using Unity.Jobs;
//using Unity.Mathematics;
//using Unity.Transforms;
//using Battle.Combat;
//using System;

//namespace Battle.Misc
//{
//    /// <summary>
//    /// Searches for all valid targets within a given radius from the picker components.
//    /// </summary>
//    [AlwaysUpdateSystem]
//    public abstract class RadialSelectionSystem : JobComponentSystem
//    {
//        bool hasRunBefore = false;
//        private EntityQuery m_targetQuery;
//        private EntityQuery m_pickerQuery;
//        private NativeArray<LocalToWorld> m_targetPositions;
//        private NativeArray<Entity> m_targetIds;
//        private NativeMultiHashMap<int, int> m_targetBins;

//        /// <summary>
//        /// Cell size used for hash map sorting
//        /// </summary>
//        public const float HASH_CELL_SIZE = 10.0f;

//        /// <summary>
//        /// Performs tasks required before the Radial search can be performed.
//        /// 
//        /// For example, allocate NativeArrays used elsewhere in system.
//        /// </summary>
//        /// <param name="inputDependencies"></param>
//        /// <returns></returns>
//        protected abstract JobHandle PrepareForSearch(JobHandle inputDependencies, int pickerEntityCount, int targetEntityCount);

//        /// <summary>
//        /// Query used to identify valid target entities.
//        /// </summary>
//        /// <returns></returns>
//        protected virtual EntityQuery CreateTargetQuery()
//        {
//            return GetEntityQuery(new EntityQueryDesc
//            {
//                All = new[] {
//                    ComponentType.ReadOnly<LocalToWorld>(),
//                }
//            });
//        }

//        protected virtual EntityQuery CreatePickerQuery()
//        {
//            return GetEntityQuery(new EntityQueryDesc
//            {
//                All = new[] {
//                    ComponentType.ReadOnly<LocalToWorld>(),
//                }
//            });
//        }

//        /// <summary>
//        /// Create a job used to process candidate entities.
//        /// </summary>
//        /// <returns></returns>
//        protected abstract JobHandle CreateProcessEntitiesInRadiusJobHandle();

//        protected override JobHandle OnUpdate(JobHandle inputDependencies)
//        {
//            // Dispose of memory allocated on previous iteration.
//            if (hasRunBefore)
//                DisposeNatives();

//            m_targetQuery.AddDependency(inputDependencies);
//            m_pickerQuery.AddDependency(inputDependencies);
//            int pickerCount = m_pickerQuery.CalculateLength();
//            int targetCount = m_targetQuery.CalculateLength();
//            m_targetBins = new NativeMultiHashMap<int, int>(targetCount, Allocator.TempJob);

//            JobHandle preJobs = PrepareForSearch(inputDependencies, pickerCount, targetCount);
//            m_targetPositions = m_targetQuery.ToComponentDataArray<LocalToWorld>(Allocator.TempJob, out var copyTargetPosJob);
//            m_targetIds = m_targetQuery.ToEntityArray(Allocator.TempJob, out JobHandle copyTargetEntityJob);
//            var entityAndPreJobs = JobHandle.CombineDependencies(copyTargetEntityJob, preJobs);

//            // Once positions are copied over, we sort the positions into a hashmap.
//            var hashTargetPosJob = new HashPositions() { CellSize = HASH_CELL_SIZE, hashMap = m_targetBins.ToConcurrent() }.Schedule(m_targetQuery, copyTargetPosJob);
//            var hashBarrier = JobHandle.CombineDependencies(hashTargetPosJob, entityAndPreJobs);

//            // Process entities in radius
//            var findTargetsJH = CreateProcessEntitiesInRadiusJobHandle();

//            hasRunBefore = true;

//            return findTargetsJH;
//        }

//        /// <summary>
//        /// Clean up NativeArrays used in the job.
//        /// </summary>
//        public virtual void DisposeNatives()
//        {
//            m_targetBins.Dispose();
//            m_targetPositions.Dispose();
//            m_targetIds.Dispose();
//        }

//        protected override void OnStopRunning()
//        {
//            // if memory exists, dispose of memory allocated on previous iteration.
//            if (hasRunBefore)
//                DisposeNatives();
//        }

//        /// <summary>
//        /// Sorts positions into a hashmap.
//        /// </summary>
//        [BurstCompile]
//        struct HashPositions : IJobForEachWithEntity<LocalToWorld>
//        {
//            public NativeMultiHashMap<int, int>.Concurrent hashMap;
//            public float CellSize;

//            public void Execute(Entity entity, int index, [ReadOnly] ref LocalToWorld localToWorld)
//            {
//                var position = localToWorld.Position;
//                float2 vec = new float2(position.x, position.z);
//                var hash = Hash(BinCoordinates(vec, CellSize));
//                hashMap.Add(hash, index);
//            }

//            public static int2 BinCoordinates(float2 position, float CellSize)
//            {
//                return new int2(math.floor(position / CellSize));
//            }

//            public static int Hash(int2 binCoords)
//            {
//                return (int)math.hash(binCoords);
//            }
//        }

//        /// <summary>
//        /// Identifies the best target for each picker.
//        /// Respects targeting orders where possible.
//        /// </summary>
//        [BurstCompile]
//        struct IdentifyBestTargetChunkJob : IJobChunk
//        {
//            public float CellSize;

//            public Action<int, int>

//            //Picker components
//            [ReadOnly] public ArchetypeChunkComponentType<LocalToWorld> PickerLocalToWorld;
//            [ReadOnly] public ArchetypeChunkComponentType<AggroRadius> PickerAggroRadii;
//            [ReadOnly] public ArchetypeChunkComponentType<Team> PickerTeams;
//            [ReadOnly] public ArchetypeChunkComponentType<TargetingOrders> PickerOrders;
//            public ArchetypeChunkComponentType<Target> PickerTargets;

//            //Target arrays
//            [ReadOnly] public NativeArray<Entity> Targets;
//            [ReadOnly] public NativeArray<Team> TargetTeams;
//            [ReadOnly] public NativeArray<LocalToWorld> TargetPositions;
//            [ReadOnly] public NativeMultiHashMap<int, int> TargetMap;
//            [ReadOnly] public NativeArray<AgentCategory.eType> TargetTypes;

//            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
//            {
//                var localToWorlds = chunk.GetNativeArray(PickerLocalToWorld);
//                var aggroRadii = chunk.GetNativeArray(PickerAggroRadii);
//                var teams = chunk.GetNativeArray(PickerTeams);

//                for (int picker = 0; picker < chunk.Count; picker++)
//                {
//                    // Ignore entities which already have a target.
//                    if (pickerTargets[picker].Value != Entity.Null)
//                        continue;

//                    // Initialise target loop variables.
//                    float score = float.PositiveInfinity;
//                    Entity currentTarget = Entity.Null;

//                    // Search all bins that cover the given aggro radius.
//                    float radius = aggroRadii[picker].Value;
//                    var pickerPosition = localToWorlds[picker].Position;
//                    float2 vec = new float2(pickerPosition.x, pickerPosition.z);
//                    var minBinCoords = HashPositions.BinCoordinates(vec - radius, CellSize);
//                    var maxBinCoords = HashPositions.BinCoordinates(vec + radius, CellSize);

//                    var orders = hasPickerOrders ? pickerOrders[picker] : defaultPickerOrders;

//                    for (int x = minBinCoords.x; x <= maxBinCoords.x; x++)
//                    {
//                        for (int y = minBinCoords.y; y <= maxBinCoords.y; y++)
//                        {
//                            // Identify bucket to search
//                            var hash = HashPositions.Hash(new int2(x, y));

//                            // Check targets within each bucket.
//                            if (!TargetMap.TryGetFirstValue(hash, out int targetIndex, out NativeMultiHashMapIterator<int> iterator))
//                                continue;
//                            CheckTarget(
//                                pickerTeams[picker],
//                                TargetTeams[targetIndex],
//                                TargetPositions[targetIndex].Position,
//                                pickerPosition,
//                                aggroRadii[picker].Value,
//                                orders,
//                                TargetTypes[targetIndex],
//                                ref score,
//                                ref currentTarget,
//                                Targets[targetIndex]
//                                );

//                            while (TargetMap.TryGetNextValue(out targetIndex, ref iterator))
//                                CheckTarget(
//                                pickerTeams[picker],
//                                TargetTeams[targetIndex],
//                                TargetPositions[targetIndex].Position,
//                                pickerPosition,
//                                aggroRadii[picker].Value,
//                                orders,
//                                TargetTypes[targetIndex],
//                                ref score,
//                                ref currentTarget,
//                                Targets[targetIndex]
//                                );
//                        }
//                    }

//                    // If a target was found, write it.
//                    if (currentTarget != Entity.Null)
//                        pickerTargets[picker] = new Target { Value = currentTarget };
//                }
//            }
//        }
//    }
//}