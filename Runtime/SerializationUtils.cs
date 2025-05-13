using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
			var state = new Entities.State(
				ReadInt(stream),
				ReadInt(stream),
				ReadInt(stream),
				(Packing)ReadByte(stream));

			entities.EnsureCapacityAt(state.UsedIds);

			stream.Read(MemoryMarshal.Cast<int, byte>(entities.Packed.AsSpan(0, state.UsedIds)));
			stream.Read(MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, state.UsedIds)));
			stream.Read(MemoryMarshal.Cast<int, byte>(entities.Sparse.AsSpan(0, state.UsedIds)));

			if (state.UsedIds < entities.UsedIds)
			{
				Array.Fill(entities.Versions, 1U, state.UsedIds, entities.UsedIds - state.UsedIds);
			}

			entities.CurrentState = state;
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

		public static unsafe void WriteAllocator(Allocator allocator, Stream stream)
		{
			WriteInt(allocator.ChunkCount, stream);
			WriteInt(allocator.UsedSpace, stream);

			stream.Write(MemoryMarshal.Cast<Chunk, byte>(allocator.Chunks.AsSpan(0, allocator.ChunkCount)));
			stream.Write(MemoryMarshal.Cast<int, byte>(allocator.ChunkFreeLists.AsSpan()));

			var underlyingType = allocator.ElementType.IsEnum ? Enum.GetUnderlyingType(allocator.ElementType) : allocator.ElementType;
			var sizeOfItem = SizeOfUnmanaged(underlyingType);
			var handle = GCHandle.Alloc(allocator.RawData, GCHandleType.Pinned);
			var span = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), allocator.UsedSpace * sizeOfItem);
			stream.Write(span);
			handle.Free();
		}

		public static unsafe void ReadAllocator(Allocator allocator, Stream stream)
		{
			var chunkCount = ReadInt(stream);
			var usedSpace = ReadInt(stream);

			allocator.EnsureChunkAt(chunkCount - 1);
			allocator.EnsureDataCapacity(usedSpace);

			stream.Read(MemoryMarshal.Cast<Chunk, byte>(allocator.Chunks.AsSpan(0, chunkCount)));
			stream.Read(MemoryMarshal.Cast<int, byte>(allocator.ChunkFreeLists.AsSpan()));

			if (chunkCount < allocator.ChunkCount)
			{
				Array.Fill(allocator.Chunks, Chunk.DefaultValid, chunkCount, allocator.ChunkCount - chunkCount);
			}

			var underlyingType = allocator.ElementType.IsEnum ? Enum.GetUnderlyingType(allocator.ElementType) : allocator.ElementType;
			var sizeOfItem = SizeOfUnmanaged(underlyingType);
			var handle = GCHandle.Alloc(allocator.RawData, GCHandleType.Pinned);
			var span = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), usedSpace * sizeOfItem);
			stream.Read(span);
			handle.Free();

			allocator.SetState(chunkCount, usedSpace);
		}

		public static void WriteAllocationTracker(AllocatorRegistry allocatorRegistry, Stream stream)
		{
			WriteInt(allocatorRegistry.UsedAllocations, stream);
			WriteInt(allocatorRegistry.NextFreeAllocation, stream);
			WriteInt(allocatorRegistry.UsedHeads, stream);

			stream.Write(MemoryMarshal.Cast<AllocatorRegistry.Allocation, byte>(
				allocatorRegistry.Allocations.AsSpan(0, allocatorRegistry.UsedAllocations)));
			stream.Write(MemoryMarshal.Cast<int, byte>(
				allocatorRegistry.Heads.AsSpan(0, allocatorRegistry.UsedHeads)));
		}

		public static void ReadAllocationTracker(AllocatorRegistry allocatorRegistry, Stream stream)
		{
			var usedAllocations = ReadInt(stream);
			var nextFreeAllocation = ReadInt(stream);
			var usedHeads = ReadInt(stream);

			allocatorRegistry.EnsureTrackerAllocationAt(usedAllocations - 1);
			allocatorRegistry.EnsureTrackerHeadAt(usedHeads - 1);

			stream.Read(MemoryMarshal.Cast<AllocatorRegistry.Allocation, byte>(
				allocatorRegistry.Allocations.AsSpan(0, usedAllocations)));
			stream.Read(MemoryMarshal.Cast<int, byte>(
				allocatorRegistry.Heads.AsSpan(0, usedHeads)));

			if (usedHeads < allocatorRegistry.UsedHeads)
			{
				Array.Fill(allocatorRegistry.Heads, Constants.InvalidId, usedHeads, allocatorRegistry.UsedHeads - usedHeads);
			}

			allocatorRegistry.SetTrackerState(usedAllocations, nextFreeAllocation, usedHeads);
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
