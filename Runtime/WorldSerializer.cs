using System;
using System.Collections.Generic;
using System.IO;

namespace Massive.Serialization
{
	public class WorldSerializer : IWorldSerializer
	{
		private readonly Dictionary<Type, IDataSerializer> _customSerializers = new Dictionary<Type, IDataSerializer>();

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
			SerializationUtils.WriteInt(world.Sets.ComponentCount, stream);
			for (var i = 0; i < world.Sets.ComponentCount; i++)
			{
				var bitSet = world.Sets.LookupByComponentId[i];
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
			SerializationUtils.WriteAllocator(world.Allocator, stream);
		}

		public void Deserialize(World world, Stream stream)
		{
			// Entities.
			SerializationUtils.ReadEntities(world.Entities, stream);

			// Sets.
			world.Sets.Reset();
			var setCount = SerializationUtils.ReadInt(stream);
			for (var i = 0; i < setCount; i++)
			{
				var setType = SerializationUtils.ReadType(stream);
				var bitSet = world.Sets.GetReflected(setType);

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

			// Clear components bitmap.
			var componentsBitMap = world.Components.BitMap;
			var componentsMaskLength = world.Components.MaskLength;
			Array.Fill(componentsBitMap, 0UL);

			// Fill components bitmap.
			for (var i = 0; i < world.Sets.ComponentCount; i++)
			{
				var bitSet = world.Sets.LookupByComponentId[i];

				var componentIndex = bitSet.ComponentIndex;
				var componentMask = bitSet.ComponentMask;
				foreach (var id in bitSet)
				{
					componentsBitMap[id * componentsMaskLength + componentIndex] |= componentMask;
				}
			}

			// Allocators.
			SerializationUtils.ReadAllocator(world.Allocator, stream);
		}
	}
}
