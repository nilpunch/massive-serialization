using System;
using System.Collections.Generic;
using System.IO;

namespace Massive.Serialization
{
	public class WorldSerializer : IWorldSerializer
	{
		private readonly Dictionary<Type, IDataSerializer> _customSerializers = new Dictionary<Type, IDataSerializer>();
		private readonly HashSet<SparseSet> _setsBuffer = new HashSet<SparseSet>();
		private readonly HashSet<Allocator> _allocatorsBuffer = new HashSet<Allocator>();
		private int[] _remap = Array.Empty<int>();

		public SerializeMode SerializeMode { get; set; } = SerializeMode.AllExceptMarked;

		public IDataSerializer DefaultUnmanagedSerializer { get; set; } = UnmanagedBinaryDataSerializer.Instance;

		public IDataSerializer DefaultManagedSerializer { get; set; } = BinaryFormatterDataSerializer.Instance;

		public void SetCustomSerializer(Type type, IDataSerializer dataSerializer)
		{
			_customSerializers[type] = dataSerializer;
		}

		public void Serialize(World world, Stream stream)
		{
			// Entities.
			SerializationUtils.WriteEntities(world.Entities, stream);

			// Sets.
			_setsBuffer.Clear();
			var setsToSerialize = _setsBuffer;
			foreach (var sparseSet in world.Sets.AllSets)
			{
				var setType = world.Sets.TypeOf(sparseSet);
				if (setType == null)
				{
					// Skip custom untyped sets (not supported yet).
					continue;
				}

				// Check serialization attributes.
				var needToSerialize = setType.IsDefined(typeof(NeedToSerialize), false);
				var doNotSerialize = setType.IsDefined(typeof(DoNotSerialize), false);
				if (needToSerialize && doNotSerialize)
				{
					throw new Exception($"[MASSIVE] Type:{setType.GetFullGenericName()} has conflictining serialization attributes.");
				}

				// Decide whether to serialize this set based on mode and attributes.
				if (SerializeMode == SerializeMode.AllExceptMarked && !doNotSerialize
					|| SerializeMode == SerializeMode.OnlyMarked && needToSerialize)
				{
					setsToSerialize.Add(sparseSet);
				}
			}

			// Write set count.
			SerializationUtils.WriteInt(setsToSerialize.Count, stream);

			// Serialize each set. Order doesn't matter.
			foreach (var sparseSet in setsToSerialize)
			{
				var setType = world.Sets.TypeOf(sparseSet);
				SerializationUtils.WriteType(setType, stream);
				SerializationUtils.WriteSparseSet(sparseSet, stream);

				// Only IDataSet has serializable data.
				if (sparseSet is not IDataSet dataSet)
				{
					continue;
				}

				// Use custom serializer if registered.
				if (_customSerializers.TryGetValue(setType, out var customSerializer))
				{
					customSerializer.Write(dataSet.Data, sparseSet.Count, stream);
					continue;
				}

				// Fallback to default serializers for managed/unmanaged types.
				if (dataSet.Data.ElementType.IsUnmanaged())
				{
					DefaultUnmanagedSerializer.Write(dataSet.Data, sparseSet.Count, stream);
				}
				else
				{
					DefaultManagedSerializer.Write(dataSet.Data, sparseSet.Count, stream);
				}
			}

			// Allocators.
			SerializationUtils.WriteInt(world.Allocators.AllAllocators.Count, stream);
			foreach (var allocator in world.Allocators.AllAllocators)
			{
				var allocatorType = world.Allocators.TypeOf(allocator);
				SerializationUtils.WriteType(allocatorType, stream);
				SerializationUtils.WriteInt(allocator.AllocatorId, stream); // Store non-deterministic ID for remapping.
				SerializationUtils.WriteAllocator(allocator, stream);
			}

			// Allocation tracker.
			SerializationUtils.WriteAllocationTracker(world.Allocators, stream);
		}

		public void Deserialize(World world, Stream stream)
		{
			// Entities.
			SerializationUtils.ReadEntities(world.Entities, stream);

			// Sets.
			_setsBuffer.Clear();
			var deserializedSets = _setsBuffer;
			var setCount = SerializationUtils.ReadInt(stream);
			for (var i = 0; i < setCount; i++)
			{
				var setType = SerializationUtils.ReadType(stream);
				var sparseSet = world.Sets.GetReflected(setType);
				deserializedSets.Add(sparseSet);

				SerializationUtils.ReadSparseSet(sparseSet, stream);

				// Only IDataSet has serializable data.
				if (sparseSet is not IDataSet dataSet)
				{
					continue;
				}

				// Use custom deserializer if registered.
				if (_customSerializers.TryGetValue(setType, out var customSerializer))
				{
					customSerializer.Read(dataSet.Data, sparseSet.Count, stream);
					continue;
				}

				// Fallback to default deserializers for managed/unmanaged types.
				if (dataSet.Data.ElementType.IsUnmanaged())
				{
					DefaultUnmanagedSerializer.Read(dataSet.Data, sparseSet.Count, stream);
				}
				else
				{
					DefaultManagedSerializer.Read(dataSet.Data, sparseSet.Count, stream);
				}
			}
			// Clear all sets that weren't deserialized.
			foreach (var sparseSet in world.Sets.AllSets)
			{
				if (!deserializedSets.Contains(sparseSet))
				{
					sparseSet.ClearWithoutNotify();
				}
			}

			// Allocators.
			_allocatorsBuffer.Clear();
			var deserializedAllocators = _allocatorsBuffer;
			var allocatorCount = SerializationUtils.ReadInt(stream);
			for (var i = 0; i < allocatorCount; i++)
			{
				var allocatorType = SerializationUtils.ReadType(stream);
				var sourceAllocatorId = SerializationUtils.ReadInt(stream);

				var allocator = world.Allocators.GetReflected(allocatorType);
				deserializedAllocators.Add(allocator);

				// Grow remap buffer if needed.
				if (sourceAllocatorId >= _remap.Length)
				{
					_remap = _remap.Resize(MathUtils.NextPowerOf2(sourceAllocatorId + 1));
				}
				_remap[sourceAllocatorId] = allocator.AllocatorId;

				SerializationUtils.ReadAllocator(allocator, stream);
			}
			// Reset all allocators that weren't deserialized.
			foreach (var allocator in world.Allocators.AllAllocators)
			{
				if (!deserializedAllocators.Contains(allocator))
				{
					allocator.Reset();
				}
			}

			// Allocation tracker.
			SerializationUtils.ReadAllocationTracker(world.Allocators, stream);

			// Remap all tracked allocations to local IDs for compatibility.
			for (var i = 0; i < world.Allocators.UsedAllocations; i++)
			{
				ref var allocation = ref world.Allocators.Allocations[i];
				allocation.AllocatorId = _remap[allocation.AllocatorId];
			}
		}
	}
}
