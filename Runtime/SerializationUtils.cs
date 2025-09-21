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
			WriteBitSet(entities, stream);

			var state = entities.CurrentState;
			WriteInt(state.PooledIds, stream);
			WriteInt(state.UsedIds, stream);

			stream.Write(MemoryMarshal.Cast<int, byte>(entities.Pool.AsSpan(0, state.PooledIds)));
			stream.Write(MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, state.UsedIds)));
		}

		public static void ReadEntities(Entities entities, Stream stream)
		{
			ReadBitSet(entities, stream);

			var state = new Entities.State(
				ReadInt(stream),
				ReadInt(stream));

			entities.EnsurePoolAt(state.PooledIds - 1);
			entities.EnsureEntityAt(state.UsedIds - 1);

			stream.Read(MemoryMarshal.Cast<int, byte>(entities.Pool.AsSpan(0, state.PooledIds)));
			stream.Read(MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, state.UsedIds)));

			if (state.UsedIds < entities.UsedIds)
			{
				Array.Fill(entities.Versions, 1U, state.UsedIds, entities.UsedIds - state.UsedIds);
			}

			entities.CurrentState = state;
		}

		public static void WriteBitSet(BitSetBase set, Stream stream)
		{
			var blocksLength = set.NonEmptyBlocks.Length;
			WriteInt(blocksLength, stream);

			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.NonEmptyBlocks.AsSpan(0, blocksLength)));
			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.SaturatedBlocks.AsSpan(0, blocksLength)));
			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.Bits.AsSpan(0, blocksLength << 6)));
		}

		public static void ReadBitSet(BitSetBase set, Stream stream)
		{
			var prevBlocksLength = set.NonEmptyBlocks.Length;

			var blocksLength = ReadInt(stream);
			set.EnsureBlocksCapacity(blocksLength);

			if (blocksLength < prevBlocksLength)
			{
				Array.Fill(set.NonEmptyBlocks, 0UL, blocksLength, prevBlocksLength - blocksLength);
				Array.Fill(set.SaturatedBlocks, 0UL, blocksLength, prevBlocksLength - blocksLength);
				Array.Fill(set.Bits, 0UL, blocksLength << 6, (prevBlocksLength - blocksLength) << 6);
			}

			stream.Read(MemoryMarshal.Cast<ulong, byte>(set.NonEmptyBlocks.AsSpan(0, blocksLength)));
			stream.Read(MemoryMarshal.Cast<ulong, byte>(set.SaturatedBlocks.AsSpan(0, blocksLength)));
			stream.Read(MemoryMarshal.Cast<ulong, byte>(set.Bits.AsSpan(0, blocksLength << 6)));
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

		public static void WriteAllocationTracker(Allocators allocators, Stream stream)
		{
			WriteInt(allocators.UsedAllocations, stream);
			WriteInt(allocators.NextFreeAllocation, stream);
			WriteInt(allocators.UsedHeads, stream);

			stream.Write(MemoryMarshal.Cast<Allocators.Allocation, byte>(
				allocators.Allocations.AsSpan(0, allocators.UsedAllocations)));
			stream.Write(MemoryMarshal.Cast<int, byte>(
				allocators.Heads.AsSpan(0, allocators.UsedHeads)));
		}

		public static void ReadAllocationTracker(Allocators allocators, Stream stream)
		{
			var usedAllocations = ReadInt(stream);
			var nextFreeAllocation = ReadInt(stream);
			var usedHeads = ReadInt(stream);

			allocators.EnsureTrackerAllocationAt(usedAllocations - 1);
			allocators.EnsureTrackerHeadAt(usedHeads - 1);

			stream.Read(MemoryMarshal.Cast<Allocators.Allocation, byte>(
				allocators.Allocations.AsSpan(0, usedAllocations)));
			stream.Read(MemoryMarshal.Cast<int, byte>(
				allocators.Heads.AsSpan(0, usedHeads)));

			if (usedHeads < allocators.UsedHeads)
			{
				Array.Fill(allocators.Heads, Constants.InvalidId, usedHeads, allocators.UsedHeads - usedHeads);
			}

			allocators.SetTrackerState(usedAllocations, nextFreeAllocation, usedHeads);
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
