using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Massive.Serialization
{
	public class UnmanagedBinaryDataSerializer : IDataSerializer
	{
		public static UnmanagedBinaryDataSerializer Instance { get; } = new UnmanagedBinaryDataSerializer();

		public unsafe void Write(IDataSet dataSet, BitSet bitSet, Stream stream)
		{
			var underlyingType = dataSet.ElementType.IsEnum ? Enum.GetUnderlyingType(dataSet.ElementType) : dataSet.ElementType;
			var sizeOfItem = ReflectionUtils.SizeOfUnmanaged(underlyingType);

			var blocksLength = bitSet.BlocksCapacity;

			var pageMasks = Constants.PageMasks;
			var deBruijn = MathUtils.DeBruijn;
			for (var blockIndex = 0; blockIndex < blocksLength; blockIndex++)
			{
				var block = bitSet.NonEmptyBlocks[blockIndex];
				var pageOffset = blockIndex << Constants.PagesInBlockPower;
				while (block != 0UL)
				{
					var blockBit = (int)deBruijn[(int)(((block & (ulong)-(long)block) * 0x37E84A99DAE458FUL) >> 58)];
					var pageIndexMod = blockBit >> Constants.PageMaskShift;

					var pageIndex = pageOffset + pageIndexMod;

					var handle = GCHandle.Alloc(dataSet.GetPage(pageIndex), GCHandleType.Pinned);
					var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), Constants.PageSize * sizeOfItem);
					stream.Write(pageAsSpan);
					handle.Free();

					block &= ~pageMasks[pageIndexMod];
				}
			}
		}

		public unsafe void Read(IDataSet dataSet, BitSet bitSet, Stream stream)
		{
			var underlyingType = dataSet.ElementType.IsEnum ? Enum.GetUnderlyingType(dataSet.ElementType) : dataSet.ElementType;
			var sizeOfItem = ReflectionUtils.SizeOfUnmanaged(underlyingType);

			var blocksLength = bitSet.BlocksCapacity;

			var pageMasks = Constants.PageMasks;
			var deBruijn = MathUtils.DeBruijn;
			for (var blockIndex = 0; blockIndex < blocksLength; blockIndex++)
			{
				var block = bitSet.NonEmptyBlocks[blockIndex];
				var pageOffset = blockIndex << Constants.PagesInBlockPower;
				while (block != 0UL)
				{
					var blockBit = (int)deBruijn[(int)(((block & (ulong)-(long)block) * 0x37E84A99DAE458FUL) >> 58)];
					var pageIndexMod = blockBit >> Constants.PageMaskShift;

					var pageIndex = pageOffset + pageIndexMod;

					dataSet.EnsurePage(pageIndex);

					var handle = GCHandle.Alloc(dataSet.GetPage(pageIndex), GCHandleType.Pinned);
					var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), Constants.PageSize * sizeOfItem);
					stream.Read(pageAsSpan);
					handle.Free();

					block &= ~pageMasks[pageIndexMod];
				}
			}
		}
	}
}
