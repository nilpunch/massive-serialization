using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Massive.Serialization
{
	public class BinaryFormatterDataSerializer : IDataSerializer
	{
		public static BinaryFormatterDataSerializer Instance { get; } = new BinaryFormatterDataSerializer();

		public class WriteScope
		{
			public Array Buffer;
			public int Count;
		}

		public class ReadScope
		{
			public int Count;
		}

		public void Write(IDataSet dataSet, BitSet bitSet, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();

			var scope = new WriteScope();
			scope.Buffer = Array.CreateInstance(dataSet.ElementType, 0);

			SerializationUtils.ForEachDataPage(dataSet, bitSet, pageIndex =>
			{
				if (scope.Count >= scope.Buffer.Length >> Constants.PageSizePower)
				{
					var growBuffer = Array.CreateInstance(dataSet.ElementType, Constants.PageSize * (scope.Count + 1) << 1);
					scope.Buffer.CopyTo(growBuffer, 0);
					scope.Buffer = growBuffer;
				}

				Array.Copy(dataSet.GetPage(pageIndex), 0, scope.Buffer, Constants.PageSize * scope.Count++, Constants.PageSize);
			});

			binaryFormatter.Serialize(stream, scope.Buffer);
		}

		public void Read(IDataSet dataSet, BitSet bitSet, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = (Array)binaryFormatter.Deserialize(stream);
			var scope = new ReadScope();

			SerializationUtils.ForEachDataPage(dataSet, bitSet, pageIndex =>
			{
				dataSet.EnsurePage(pageIndex);

				Array.Copy(buffer, Constants.PageSize * scope.Count++, dataSet.GetPage(pageIndex), 0, Constants.PageSize);
			});
		}
	}
}
