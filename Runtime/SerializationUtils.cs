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
			var blocksLength = set.BlocksCapacity;
			WriteInt(blocksLength, stream);

			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.NonEmptyBlocks.AsSpan(0, blocksLength)));
			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.SaturatedBlocks.AsSpan(0, blocksLength)));
			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.Bits.AsSpan(0, blocksLength << 6)));
		}

		public static void ReadBitSet(BitSetBase set, Stream stream)
		{
			var prevBlocksLength = set.BlocksCapacity;

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
			WriteInt(allocator.PageCount, stream);

			stream.Write(new Span<byte>(allocator.Pages[0].AlignedPtr, allocator.Pages[0].FullPageLength));

			for (var i = 1; i < allocator.PageCount; i++)
			{
				ref var page = ref allocator.Pages[i];
				WriteInt(page.SlotClass, stream);
				stream.Write(new Span<byte>(page.AlignedPtr, page.FullPageLength));
			}

			stream.Write(MemoryMarshal.Cast<Pointer, byte>(allocator.NextToAlloc.AsSpan(0, Allocator.AllClassCount)));
			stream.Write(MemoryMarshal.Cast<Pointer, byte>(allocator.FreeToAlloc.AsSpan(0, Allocator.AllClassCount)));
		}

		public static unsafe void ReadAllocator(Allocator allocator, Stream stream)
		{
			var pageCount = ReadInt(stream);

			allocator.Reset();
			allocator.EnsurePageAt(pageCount - 1);
			allocator.SetPageCount((ushort)pageCount);

			stream.Read(new Span<byte>(allocator.Pages[0].AlignedPtr, allocator.Pages[0].FullPageLength));

			for (var i = 1; i < pageCount; i++)
			{
				ref var page = ref allocator.Pages[i];

				var slotClass = ReadInt(stream);
				var pageLength = Allocator.PageLength(slotClass);
				var bitSetLength = Allocator.BitSetLength(slotClass);

				page = new Allocator.Page(UnsafeUtils.AllocAligned(pageLength + bitSetLength, Allocator.MinPageLength), slotClass);
				stream.Read(new Span<byte>(page.AlignedPtr, page.FullPageLength));
			}

			stream.Read(MemoryMarshal.Cast<Pointer, byte>(allocator.NextToAlloc.AsSpan(0, Allocator.AllClassCount)));
			stream.Read(MemoryMarshal.Cast<Pointer, byte>(allocator.FreeToAlloc.AsSpan(0, Allocator.AllClassCount)));
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
	}
}
