using System;
using System.Collections.Generic;
using System.IO;

namespace Massive.Serialization
{
	public class RegistrySerializer : IRegistrySerializer
	{
		private readonly Dictionary<Type, IDataSerializer> _customSerializers = new Dictionary<Type, IDataSerializer>();

		private readonly HashSet<SparseSet> _deserializedSetsBuffer = new HashSet<SparseSet>();

		public void AddCustomSerializer(Type type, IDataSerializer dataSerializer)
		{
			_customSerializers[type] = dataSerializer;
		}

		public void Serialize(World world, Stream stream)
		{
			// Entities.
			SerializationUtils.WriteEntities(world.Entities, stream);

			// Sets.
			SerializationUtils.WriteInt(world.SetRegistry.AllSets.Count, stream);
			foreach (var sparseSet in world.SetRegistry.AllSets)
			{
				var setType = world.SetRegistry.TypeOf(sparseSet);
				SerializationUtils.WriteType(setType, stream);
				SerializationUtils.WriteSparseSet(sparseSet, stream);

				if (sparseSet is not IDataSet dataSet)
				{
					continue;
				}

				if (_customSerializers.TryGetValue(setType, out var customSerializer))
				{
					customSerializer.Write(dataSet.Data, sparseSet.Count, stream);
					continue;
				}

				if (dataSet.Data.ElementType.IsUnmanaged())
				{
					SerializationUtils.WriteUnmanagedPagedArray(dataSet.Data, sparseSet.Count, stream);
				}
				else
				{
					SerializationUtils.WriteManagedPagedArray(dataSet.Data, sparseSet.Count, stream);
				}
			}
		}

		public void Deserialize(World world, Stream stream)
		{
			// Entities.
			SerializationUtils.ReadEntities(world.Entities, stream);

			// Sets.
			_deserializedSetsBuffer.Clear();
			var setCount = SerializationUtils.ReadInt(stream);
			for (var i = 0; i < setCount; i++)
			{
				var setKey = SerializationUtils.ReadType(stream);

				var sparseSet = world.SetRegistry.GetReflected(setKey);
				_deserializedSetsBuffer.Add(sparseSet);

				SerializationUtils.ReadSparseSet(sparseSet, stream);

				if (sparseSet is not IDataSet dataSet)
				{
					continue;
				}

				if (_customSerializers.TryGetValue(setKey, out var customSerializer))
				{
					customSerializer.Read(dataSet.Data, sparseSet.Count, stream);
					continue;
				}

				if (dataSet.Data.ElementType.IsUnmanaged())
				{
					SerializationUtils.ReadUnmanagedPagedArray(dataSet.Data, sparseSet.Count, stream);
				}
				else
				{
					SerializationUtils.ReadManagedPagedArray(dataSet.Data, sparseSet.Count, stream);
				}
			}
			// Clear all remaining sets.
			foreach (var sparseSet in world.SetRegistry.AllSets)
			{
				if (!_deserializedSetsBuffer.Contains(sparseSet))
				{
					sparseSet.ClearWithoutNotify();
				}
			}
		}
	}
}
