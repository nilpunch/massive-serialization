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
		public static void WriteEntities(this Stream stream, Entities entities)
		{
			WriteBitSet(stream, entities);

			var state = entities.CurrentState;
			WriteInt(stream, state.PooledIds);
			WriteInt(stream, state.UsedIds);

			stream.Write(MemoryMarshal.Cast<int, byte>(entities.Pool.AsSpan(0, state.PooledIds)));
			stream.Write(MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, state.UsedIds)));
		}

		public static void ReadEntities(this Stream stream, Entities entities)
		{
			ReadBitSet(stream, entities);

			var state = new Entities.State(
				ReadInt(stream),
				ReadInt(stream));

			entities.EnsurePoolAt(state.PooledIds - 1);
			entities.EnsureEntityAt(state.UsedIds - 1);

			ReadExactly(stream, MemoryMarshal.Cast<int, byte>(entities.Pool.AsSpan(0, state.PooledIds)));
			ReadExactly(stream, MemoryMarshal.Cast<uint, byte>(entities.Versions.AsSpan(0, state.UsedIds)));

			if (state.UsedIds < entities.UsedIds)
			{
				Array.Fill(entities.Versions, 1U, state.UsedIds, entities.UsedIds - state.UsedIds);
			}

			entities.CurrentState = state;
		}

		public static void WriteBitSet(this Stream stream, BitSetBase set)
		{
			var blocksLength = set.BlocksCapacity;
			WriteInt(stream, blocksLength);

			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.NonEmptyBlocks.AsSpan(0, blocksLength)));
			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.SaturatedBlocks.AsSpan(0, blocksLength)));
			stream.Write(MemoryMarshal.Cast<ulong, byte>(set.Bits.AsSpan(0, blocksLength << 6)));
		}

		public static void ReadBitSet(this Stream stream, BitSetBase set)
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

			ReadExactly(stream, MemoryMarshal.Cast<ulong, byte>(set.NonEmptyBlocks.AsSpan(0, blocksLength)));
			ReadExactly(stream, MemoryMarshal.Cast<ulong, byte>(set.SaturatedBlocks.AsSpan(0, blocksLength)));
			ReadExactly(stream, MemoryMarshal.Cast<ulong, byte>(set.Bits.AsSpan(0, blocksLength << 6)));
		}

		public static unsafe void WriteAllocator(this Stream stream, Allocator allocator)
		{
			WriteInt(stream, allocator.PageCount);

			stream.Write(new Span<byte>(allocator.Pages[0].AlignedPtr, allocator.Pages[0].PageLengthWithBitset));

			for (var i = 1; i < allocator.PageCount; i++)
			{
				ref var page = ref allocator.Pages[i];
				WriteInt(stream, page.SlotClass);
				stream.Write(new Span<byte>(page.AlignedPtr, page.PageLengthWithBitset));
			}

			stream.Write(MemoryMarshal.Cast<Pointer, byte>(allocator.NextToAlloc.AsSpan(0, Allocator.AllClassCount)));
			stream.Write(MemoryMarshal.Cast<Pointer, byte>(allocator.FreeToAlloc.AsSpan(0, Allocator.AllClassCount)));
		}

		public static unsafe void ReadAllocator(this Stream stream, Allocator allocator)
		{
			var pageCount = ReadInt(stream);

			allocator.Reset();
			allocator.EnsurePageAt(pageCount - 1);
			allocator.SetPageCount((ushort)pageCount);

			ReadExactly(stream, new Span<byte>(allocator.Pages[0].AlignedPtr, allocator.Pages[0].PageLengthWithBitset));

			for (var i = 1; i < pageCount; i++)
			{
				ref var page = ref allocator.Pages[i];

				var slotClass = ReadInt(stream);
				var pageLength = Allocator.PageLength(slotClass);
				var bitSetLength = Allocator.BitSetLength(slotClass);

				page = new Allocator.Page(UnsafeUtils.AllocAligned(pageLength + bitSetLength, Allocator.MinPageLength), slotClass);
				ReadExactly(stream, new Span<byte>(page.AlignedPtr, page.PageLengthWithBitset));
			}

			ReadExactly(stream, MemoryMarshal.Cast<Pointer, byte>(allocator.NextToAlloc.AsSpan(0, Allocator.AllClassCount)));
			ReadExactly(stream, MemoryMarshal.Cast<Pointer, byte>(allocator.FreeToAlloc.AsSpan(0, Allocator.AllClassCount)));
		}

		public static void WriteInt(this Stream stream, int value)
		{
			Span<byte> buffer = stackalloc byte[sizeof(int)];
			BitConverter.TryWriteBytes(buffer, value);
			stream.Write(buffer);
		}

		public static int ReadInt(this Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(int)];
			ReadExactly(stream, buffer);
			return BitConverter.ToInt32(buffer);
		}

		public static void WriteDouble(this Stream stream, double value)
		{
			Span<byte> buffer = stackalloc byte[sizeof(double)];
			BitConverter.TryWriteBytes(buffer, value);
			stream.Write(buffer);
		}

		public static double ReadDouble(this Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(double)];
			ReadExactly(stream, buffer);
			return BitConverter.ToDouble(buffer);
		}

		public static void WriteShort(this Stream stream, short value)
		{
			Span<byte> buffer = stackalloc byte[sizeof(short)];
			BitConverter.TryWriteBytes(buffer, value);
			stream.Write(buffer);
		}

		public static short ReadShort(this Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(short)];
			ReadExactly(stream, buffer);
			return BitConverter.ToInt16(buffer);
		}

		public static void Write1Byte(this Stream stream, byte value)
		{
			Span<byte> buffer = stackalloc byte[sizeof(byte)];
			buffer[0] = value;
			stream.Write(buffer);
		}

		public static byte Read1Byte(this Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(byte)];
			ReadExactly(stream, buffer);
			return buffer[0];
		}

		public static void WriteBool(this Stream stream, bool value)
		{
			Span<byte> buffer = stackalloc byte[sizeof(bool)];
			BitConverter.TryWriteBytes(buffer, value);
			stream.Write(buffer);
		}

		public static bool ReadBool(this Stream stream)
		{
			Span<byte> buffer = stackalloc byte[sizeof(bool)];
			ReadExactly(stream, buffer);
			return BitConverter.ToBoolean(buffer);
		}

		public static void WriteType(this Stream stream, Type type)
		{
			var typeName = type.AssemblyQualifiedName!;
			var nameBuffer = Encoding.UTF8.GetBytes(typeName);

			WriteInt(stream, nameBuffer.Length);
			stream.Write(nameBuffer);
		}

		public static Type ReadType(this Stream stream)
		{
			var nameLength = ReadInt(stream);
			var nameBuffer = new byte[nameLength];

			ReadExactly(stream, nameBuffer);

			var typeName = Encoding.UTF8.GetString(nameBuffer);
			return Type.GetType(typeName, true);
		}

		public static void ReadExactly(this Stream stream, Span<byte> buffer)
		{
			var read = 0;
			while (read < buffer.Length)
			{
				var n = stream.Read(buffer.Slice(read));
				if (n == 0)
				{
					throw new EndOfStreamException();
				}
				read += n;
			}
		}
	}
}
