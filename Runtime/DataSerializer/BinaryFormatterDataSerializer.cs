#if !NET9_0_OR_GREATER
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Massive.Serialization
{
	public class BinaryFormatterDataSerializer : IDataSerializer
	{
		public static BinaryFormatterDataSerializer Instance { get; } = new BinaryFormatterDataSerializer();

		public void Write(IDataSet dataSet, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = Array.CreateInstance(dataSet.ElementType, 0);
			var count = 0;

			foreach (var pageIndex in dataSet.GetDataPages())
			{
				if (count >= buffer.Length >> Constants.PageSizePower)
				{
					var growBuffer = Array.CreateInstance(dataSet.ElementType, Constants.PageSize * ((count + 1) << 1));
					buffer.CopyTo(growBuffer, 0);
					buffer = growBuffer;
				}

				Array.Copy(dataSet.GetPage(pageIndex), 0, buffer, Constants.PageSize * count++, Constants.PageSize);
			}

			binaryFormatter.Serialize(stream, buffer);
		}

		public void Read(IDataSet dataSet, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = (Array)binaryFormatter.Deserialize(stream);
			var count = 0;

			foreach (var pageIndex in dataSet.GetDataPages())
			{
				dataSet.EnsurePage(pageIndex);

				Array.Copy(buffer, Constants.PageSize * count++, dataSet.GetPage(pageIndex), 0, Constants.PageSize);
			}
		}
	}
}
#endif
