using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

// ReSharper disable MustUseReturnValue
namespace Massive.Serialization
{
	public static class SerializationUtils
	{
		public static void WriteEntities(Entities entities, Stream stream)
		{
			var state = entities.CurrentState;
			WriteInt(state.Count, stream);
			WriteInt(state.UsedIds, stream);
			WriteInt(state.NextHoleId, stream);
			WriteByte((byte)state.Packing, stream);

			stream.Write(MemoryMarshal.Cast<int, byte>(entities.Packed.AsSpan(0, entities.UsedIds)));
			stream.Write(MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, entities.UsedIds)));
			stream.Write(MemoryMarshal.Cast<int, byte>(entities.Sparse.AsSpan(0, entities.UsedIds)));
		}

		public static void ReadEntities(Entities entities, Stream stream)
		{
			entities.CurrentState = new Entities.State(
				ReadInt(stream),
				ReadInt(stream),
				ReadInt(stream),
				(Packing)ReadByte(stream));

			entities.EnsureCapacityAt(entities.UsedIds);

			stream.Read(MemoryMarshal.Cast<int, byte>(entities.Packed.AsSpan(0, entities.UsedIds)));
			stream.Read(MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, entities.UsedIds)));
			stream.Read(MemoryMarshal.Cast<int, byte>(entities.Sparse.AsSpan(0, entities.UsedIds)));
		}

		public static void WriteSparseSet(SparseSet set, Stream stream)
		{
			var state = set.CurrentState;
			WriteInt(state.Count, stream);
			WriteInt(state.UsedIds, stream);
			WriteInt(state.NextHole, stream);
			WriteByte((byte)state.Packing, stream);

			stream.Write(MemoryMarshal.Cast<int, byte>(set.Packed.AsSpan(0, set.Count)));
			stream.Write(MemoryMarshal.Cast<int, byte>(set.Sparse.AsSpan(0, set.UsedIds)));
		}

		public static void ReadSparseSet(SparseSet set, Stream stream)
		{
			var state = new SparseSet.State(
				ReadInt(stream),
				ReadInt(stream),
				ReadInt(stream),
				(Packing)ReadByte(stream));

			set.EnsurePackedAt(state.Count - 1);
			set.EnsureSparseAt(state.UsedIds - 1);

			stream.Read(MemoryMarshal.Cast<int, byte>(set.Packed.AsSpan(0, state.Count)));
			stream.Read(MemoryMarshal.Cast<int, byte>(set.Sparse.AsSpan(0, state.UsedIds)));

			if (state.UsedIds < set.UsedIds)
			{
				Array.Fill(set.Sparse, Constants.InvalidId, state.UsedIds, set.UsedIds - state.UsedIds);
			}

			set.CurrentState = state;
		}

		public static void WriteInt(int value, Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(int)];
			BitConverter.TryWriteBytes(buffer, value);
			stream.Write(buffer);
		}

		public static int ReadInt(Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(int)];
			stream.Read(buffer);
			return BitConverter.ToInt32(buffer);
		}

		public static void WriteByte(byte value, Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(byte)];
			buffer[0] = value;
			stream.Write(buffer);
		}

		public static byte ReadByte(Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(byte)];
			stream.Read(buffer);
			return buffer[0];
		}

		public static void WriteBool(bool value, Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(bool)];
			BitConverter.TryWriteBytes(buffer, value);
			stream.Write(buffer);
		}

		public static bool ReadBool(Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(bool)];
			stream.Read(buffer);
			return BitConverter.ToBoolean(buffer);
		}

		public static void WriteType(Type type, Stream stream)
		{
			var typeName = type.AssemblyQualifiedName!;
			var nameBuffer = Encoding.UTF8.GetBytes(typeName);

			WriteInt(nameBuffer.Length, stream);
			stream.Write(nameBuffer);
		}

		public static Type ReadType(Stream stream)
		{
			var nameLength = ReadInt(stream);
			var nameBuffer = new byte[nameLength];

			stream.Read(nameBuffer);

			var typeName = Encoding.UTF8.GetString(nameBuffer);
			return Type.GetType(typeName, true);
		}

		private static readonly Dictionary<Type, int> s_sizeOfCache = new Dictionary<Type, int>();

		private static unsafe int SizeOf<T>() where T : unmanaged => sizeof(T);

		public static int SizeOfUnmanaged(Type t)
		{
			if (!s_sizeOfCache.TryGetValue(t, out var size))
			{
				var genericMethod = typeof(SerializationUtils)
					.GetMethod(nameof(SizeOf), BindingFlags.Static | BindingFlags.NonPublic)
					.MakeGenericMethod(t);
				size = (int)genericMethod.Invoke(null, new object[] { });
				s_sizeOfCache.Add(t, size);
			}

			return size;
		}
	}
}
