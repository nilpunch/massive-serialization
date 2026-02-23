using System;
using System.Collections.Generic;
using System.IO;

namespace Massive.Serialization
{
	public class WorldSerializer : IWorldSerializer
	{
		private readonly Dictionary<Type, IDataSerializer> _customSerializers = new Dictionary<Type, IDataSerializer>();

		public ITypeSerializer TypeSerializer { get; set; } = AQNTypeSerializer.Instance;

		public IDataSerializer DefaultUnmanagedSerializer { get; set; } = UnmanagedBinaryDataSerializer.Instance;

		public IDataSerializer DefaultManagedSerializer { get; set; } =
#if NET9_0_OR_GREATER || GODOT
			DataContractDataSerializer.Instance;
#else
			BinaryFormatterDataSerializer.Instance;
#endif

		public void SetCustomSerializer(Type type, IDataSerializer dataSerializer)
		{
			_customSerializers[type] = dataSerializer;
		}

		public void Serialize(World world, Stream stream)
		{
			// Entities.
			stream.WriteEntities(world.Entities);

			// Sets.
			stream.WriteInt(world.Sets.ComponentCount);
			for (var i = 0; i < world.Sets.ComponentCount; i++)
			{
				var bitSet = world.Sets.LookupByComponentId[i];
				var setType = world.Sets.TypeOf(bitSet);

				TypeSerializer.Serialize(setType, stream);

				stream.WriteBitSet(bitSet);

				// Only IDataSet has serializable data.
				if (bitSet is not IDataSet dataSet)
				{
					continue;
				}

				// Use custom serializer if registered.
				if (_customSerializers.TryGetValue(setType, out var customSerializer))
				{
					customSerializer.Write(dataSet, stream);
					continue;
				}

				// Fallback to default serializers for managed/unmanaged types.
				if (dataSet.ElementType.IsUnmanaged())
				{
					DefaultUnmanagedSerializer.Write(dataSet, stream);
				}
				else
				{
					DefaultManagedSerializer.Write(dataSet, stream);
				}
			}

			// Allocator.
			stream.WriteAllocator(world.Allocator);
		}

		public void Deserialize(World world, Stream stream)
		{
			// Entities.
			stream.ReadEntities(world.Entities);

			// Sets.
			world.Sets.Reset();
			var setCount = stream.ReadInt();
			for (var i = 0; i < setCount; i++)
			{
				var setType = TypeSerializer.Deserialize(stream);
				var bitSet = world.Sets.GetReflected(setType);
				stream.ReadBitSet(bitSet);

				world.Sets.EnsureBinded(bitSet);

				// Only IDataSet has serializable data.
				if (bitSet is not IDataSet dataSet)
				{
					continue;
				}

				// Use custom deserializer if registered.
				if (_customSerializers.TryGetValue(setType, out var customSerializer))
				{
					customSerializer.Read(dataSet, stream);
					continue;
				}

				// Fallback to default deserializers for managed/unmanaged types.
				if (dataSet.ElementType.IsUnmanaged())
				{
					DefaultUnmanagedSerializer.Read(dataSet, stream);
				}
				else
				{
					DefaultManagedSerializer.Read(dataSet, stream);
				}
			}

			// Allocator.
			stream.ReadAllocator(world.Allocator);

			// Clear components bitmap.
			world.Components.Reset();

			// Fill components bitmap.
			var componentsBitMap = world.Components.BitMap;
			var componentsMaskLength = world.Components.MaskLength;
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
		}
	}
}
