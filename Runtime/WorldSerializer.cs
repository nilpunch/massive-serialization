using System;
using System.Collections.Generic;
using System.IO;

namespace Massive.Serialization
{
	public class WorldSerializer : IWorldSerializer
	{
		private readonly Dictionary<Type, IDataSerializer> _customSerializers = new Dictionary<Type, IDataSerializer>();
		private readonly HashSet<BitSet> _setsBuffer = new HashSet<BitSet>();
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
			foreach (var bitSet in world.Sets.Sorted)
			{
				var setType = world.Sets.TypeOf(bitSet);

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
					setsToSerialize.Add(bitSet);
				}
			}

			// Write set count.
			SerializationUtils.WriteInt(setsToSerialize.Count, stream);

			// Serialize each set.
			foreach (var bitSet in setsToSerialize)
			{
				var setType = world.Sets.TypeOf(bitSet);
				SerializationUtils.WriteType(setType, stream);
				SerializationUtils.WriteBitSet(bitSet, stream);

				// Only IDataSet has serializable data.
				if (bitSet is not IDataSet dataSet)
				{
					continue;
				}

				// Use custom serializer if registered.
				if (_customSerializers.TryGetValue(setType, out var customSerializer))
				{
					customSerializer.Write(dataSet, bitSet, stream);
					continue;
				}

				// Fallback to default serializers for managed/unmanaged types.
				if (dataSet.ElementType.IsUnmanaged())
				{
					DefaultUnmanagedSerializer.Write(dataSet, bitSet, stream);
				}
				else
				{
					DefaultManagedSerializer.Write(dataSet, bitSet, stream);
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
				var bitSet = world.Sets.GetReflected(setType);
				deserializedSets.Add(bitSet);

				SerializationUtils.ReadBitSet(bitSet, stream);

				// Only IDataSet has serializable data.
				if (bitSet is not IDataSet dataSet)
				{
					continue;
				}

				// Use custom deserializer if registered.
				if (_customSerializers.TryGetValue(setType, out var customSerializer))
				{
					customSerializer.Read(dataSet, bitSet, stream);
					continue;
				}

				// Fallback to default deserializers for managed/unmanaged types.
				if (dataSet.ElementType.IsUnmanaged())
				{
					DefaultUnmanagedSerializer.Read(dataSet, bitSet, stream);
				}
				else
				{
					DefaultManagedSerializer.Read(dataSet, bitSet, stream);
				}
			}

			// Clear all sets that weren't deserialized and update components.
			var componentsBitMap = world.Components.BitMap;
			var componentsMaskLength = world.Components.MaskLength;
			Array.Fill(componentsBitMap, 0UL);
			foreach (var bitSet in world.Sets.Sorted)
			{
				if (!deserializedSets.Contains(bitSet))
				{
					bitSet.ClearWithoutNotify();
					continue;
				}

				var componentIndex = bitSet.ComponentIndex;
				var componentMask = bitSet.ComponentMask;
				foreach (var id in bitSet)
				{
					componentsBitMap[id * componentsMaskLength + componentIndex] |= componentMask;
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
