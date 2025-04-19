using System;
using System.Collections.Generic;
using System.IO;

namespace Massive.Serialization
{
	public class RegistrySerializer : IRegistrySerializer
	{
		private readonly Dictionary<Type, IDataSerializer> _customSerializers = new Dictionary<Type, IDataSerializer>();
		private readonly HashSet<SparseSet> _setsBuffer = new HashSet<SparseSet>();

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
			foreach (var sparseSet in world.SetRegistry.AllSets)
			{
				var setType = world.SetRegistry.TypeOf(sparseSet);
				if (setType == null)
				{
					// TODO: Serialization for custom untyped sets.
					continue;
				}

				var needToSerialize = setType.IsDefined(typeof(NeedToSerialize), false);
				var doNotSerialize = setType.IsDefined(typeof(DoNotSerialize), false);
				if (needToSerialize && doNotSerialize)
				{
					throw new Exception($"[MASSIVE] Type:{setType.GetFullGenericName()} has conflictining serialization attributes.");
				}

				if (SerializeMode == SerializeMode.AllExceptMarked && !doNotSerialize
					|| SerializeMode == SerializeMode.OnlyMarked && needToSerialize)
				{
					setsToSerialize.Add(sparseSet);
				}
			}

			SerializationUtils.WriteInt(setsToSerialize.Count, stream);

			// No need to maintain order — SetRegistry takes care of sorting.
			foreach (var sparseSet in setsToSerialize)
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
					DefaultUnmanagedSerializer.Write(dataSet.Data, sparseSet.Count, stream);
				}
				else
				{
					DefaultManagedSerializer.Write(dataSet.Data, sparseSet.Count, stream);
				}
			}
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

				var sparseSet = world.SetRegistry.GetReflected(setType);
				deserializedSets.Add(sparseSet);

				SerializationUtils.ReadSparseSet(sparseSet, stream);

				if (sparseSet is not IDataSet dataSet)
				{
					continue;
				}

				if (_customSerializers.TryGetValue(setType, out var customSerializer))
				{
					customSerializer.Read(dataSet.Data, sparseSet.Count, stream);
					continue;
				}

				if (dataSet.Data.ElementType.IsUnmanaged())
				{
					DefaultUnmanagedSerializer.Read(dataSet.Data, sparseSet.Count, stream);
				}
				else
				{
					DefaultManagedSerializer.Read(dataSet.Data, sparseSet.Count, stream);
				}
			}
			// Clear all remaining sets.
			foreach (var sparseSet in world.SetRegistry.AllSets)
			{
				if (!deserializedSets.Contains(sparseSet))
				{
					sparseSet.ClearWithoutNotify();
				}
			}
		}
	}
}
