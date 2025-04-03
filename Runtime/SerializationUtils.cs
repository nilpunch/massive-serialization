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
			WriteInt(state.MaxId, stream);
			WriteInt(state.NextHoleId, stream);
			WriteByte((byte)state.Packing, stream);

			stream.Write(MemoryMarshal.Cast<int, byte>(entities.Packed.AsSpan(0, entities.MaxId)));
			stream.Write(MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, entities.MaxId)));
			stream.Write(MemoryMarshal.Cast<int, byte>(entities.Sparse.AsSpan(0, entities.MaxId)));
		}

		public static void ReadEntities(Entities entities, Stream stream)
		{
			entities.CurrentState = new Entities.State(
				ReadInt(stream),
				ReadInt(stream),
				ReadInt(stream),
				(Packing)ReadByte(stream));

			entities.EnsureCapacityAt(entities.MaxId);

			stream.Read(MemoryMarshal.Cast<int, byte>(entities.Packed.AsSpan(0, entities.MaxId)));
			stream.Read(MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, entities.MaxId)));
			stream.Read(MemoryMarshal.Cast<int, byte>(entities.Sparse.AsSpan(0, entities.MaxId)));
		}

		public static void WriteSparseSet(SparseSet set, Stream stream)
		{
			var state = set.CurrentState;
			WriteInt(state.Count, stream);
			WriteInt(state.UsedIds, stream);
			WriteInt(state.NextHole, stream);
			WriteByte((byte)state.Packing, stream);

			WriteInt(set.SparseCapacity, stream);

			stream.Write(MemoryMarshal.Cast<int, byte>(set.Packed.AsSpan(0, set.Count)));
			stream.Write(MemoryMarshal.Cast<int, byte>(set.Sparse.AsSpan(0, set.SparseCapacity)));
		}

		public static void ReadSparseSet(SparseSet set, Stream stream)
		{
			set.CurrentState = new SparseSet.State(
				ReadInt(stream),
				ReadInt(stream),
				ReadInt(stream),
				(Packing)ReadByte(stream));

			var sparseCapacity = ReadInt(stream);

			set.EnsurePackedAt(set.Count - 1);
			set.EnsureSparseAt(sparseCapacity - 1);

			stream.Read(MemoryMarshal.Cast<int, byte>(set.Packed.AsSpan(0, set.Count)));
			stream.Read(MemoryMarshal.Cast<int, byte>(set.Sparse.AsSpan(0, sparseCapacity)));
			if (sparseCapacity < set.SparseCapacity)
			{
				Array.Fill(set.Sparse, Constants.InvalidId, sparseCapacity, set.SparseCapacity - sparseCapacity);
			}
		}

		public static unsafe void WriteUnmanagedPagedArray(IPagedArray pagedArray, int count, Stream stream)
		{
			var underlyingType = pagedArray.ElementType.IsEnum ? Enum.GetUnderlyingType(pagedArray.ElementType) : pagedArray.ElementType;
			var sizeOfItem = SizeOfUnmanaged(underlyingType);

			foreach (var page in new PageSequence(pagedArray.PageSize, count))
			{
				var handle = GCHandle.Alloc(pagedArray.GetPage(page.Index), GCHandleType.Pinned);
				var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), page.Length * sizeOfItem);
				stream.Write(pageAsSpan);
				handle.Free();
			}
		}

		public static unsafe void ReadUnmanagedPagedArray(IPagedArray pagedArray, int count, Stream stream)
		{
			var underlyingType = pagedArray.ElementType.IsEnum ? Enum.GetUnderlyingType(pagedArray.ElementType) : pagedArray.ElementType;
			var sizeOfItem = SizeOfUnmanaged(underlyingType);

			foreach (var page in new PageSequence(pagedArray.PageSize, count))
			{
				pagedArray.EnsurePage(page.Index);

				var handle = GCHandle.Alloc(pagedArray.GetPage(page.Index), GCHandleType.Pinned);
				var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), page.Length * sizeOfItem);
				stream.Read(pageAsSpan);
				handle.Free();
			}
		}

		public static unsafe void WriteUnmanagedArray(Array array, int count, Stream stream)
		{
			var elementType = array.GetType().GetElementType()!;
			var underlyingType = elementType.IsEnum ? Enum.GetUnderlyingType(elementType) : elementType;
			var sizeOfItem = SizeOfUnmanaged(underlyingType);

			var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
			var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), count * sizeOfItem);
			stream.Write(pageAsSpan);
			handle.Free();
		}

		public static unsafe void ReadUnmanagedArray(Array array, int count, Stream stream)
		{
			var elementType = array.GetType().GetElementType()!;
			var underlyingType = elementType.IsEnum ? Enum.GetUnderlyingType(elementType) : elementType;
			var sizeOfItem = SizeOfUnmanaged(underlyingType);

			var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
			var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), count * sizeOfItem);
			stream.Read(pageAsSpan);
			handle.Free();
		}

		public static void WriteManagedPagedArray(IPagedArray pagedArray, int count, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = Array.CreateInstance(pagedArray.ElementType, count);

			foreach (var page in new PageSequence(pagedArray.PageSize, count))
			{
				Array.Copy(pagedArray.GetPage(page.Index), 0, buffer, page.Offset, page.Length);
			}

			binaryFormatter.Serialize(stream, buffer);
		}

		public static void ReadManagedPagedArray(IPagedArray pagedArray, int count, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = (Array)binaryFormatter.Deserialize(stream);

			foreach (var page in new PageSequence(pagedArray.PageSize, count))
			{
				pagedArray.EnsurePage(page.Index);
				Array.Copy(buffer, page.Offset, pagedArray.GetPage(page.Index), 0, page.Length);
			}
		}

		public static void WriteManagedArray(Array array, int count, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = Array.CreateInstance(array.GetType().GetElementType()!, count);

			Array.Copy(array, buffer, count);

			binaryFormatter.Serialize(stream, buffer);
		}

		public static void ReadManagedArray(Array array, int count, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = (Array)binaryFormatter.Deserialize(stream);

			Array.Copy(buffer, array, count);
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

		private unsafe static int SizeOf<T>() where T : unmanaged => sizeof(T);

		public static int SizeOfUnmanaged(Type t)
		{
			if (!s_sizeOfCache.TryGetValue(t, out var size))
			{
				if (t.IsGenericType)
				{
					var genericMethod = typeof(SerializationUtils)
						.GetMethod(nameof(SizeOf), BindingFlags.Static | BindingFlags.NonPublic)
						.MakeGenericMethod(t);
					size = (int)genericMethod.Invoke(null, new object[] { });
				}
				else
				{
					size = Marshal.SizeOf(t);
				}
				s_sizeOfCache.Add(t, size);
			}

			return size;
		}
	}
}
