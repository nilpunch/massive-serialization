using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Massive.Serialization
{
	public class BinaryFormatterDataSerializer : IDataSerializer
	{
		public static BinaryFormatterDataSerializer Instance { get; } = new BinaryFormatterDataSerializer();

		public void Write(IDataSet dataSet, BitSet bitSet, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = Array.CreateInstance(dataSet.ElementType, 0);
			var count = 0;

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

					if (count >= buffer.Length >> Constants.PageSizePower)
					{
						var growBuffer = Array.CreateInstance(dataSet.ElementType, Constants.PageSize * (count + 1) << 1);
						buffer.CopyTo(growBuffer, 0);
						buffer = growBuffer;
					}

					Array.Copy(dataSet.GetPage(pageIndex), 0, buffer, Constants.PageSize * count++, Constants.PageSize);

					block &= ~pageMasks[pageIndexMod];
				}
			}

			binaryFormatter.Serialize(stream, buffer);
		}

		public void Read(IDataSet dataSet, BitSet bitSet, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = (Array)binaryFormatter.Deserialize(stream);
			var count = 0;

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

					Array.Copy(buffer, Constants.PageSize * count++, dataSet.GetPage(pageIndex), 0, Constants.PageSize);

					block &= ~pageMasks[pageIndexMod];
				}
			}
		}
	}
}
