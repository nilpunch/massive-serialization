using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Massive.Serialization
{
	public class BinaryFormatterDataSerializer : IDataSerializer
	{
		public static BinaryFormatterDataSerializer Instance { get; } = new BinaryFormatterDataSerializer();

		public void Write(IPagedArray pagedArray, int count, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = Array.CreateInstance(pagedArray.ElementType, count);

			foreach (var page in new PageSequence(pagedArray.PageSize, count))
			{
				Array.Copy(pagedArray.GetPage(page.Index), 0, buffer, page.Offset, page.Length);
			}

			binaryFormatter.Serialize(stream, buffer);
		}

		public void Read(IPagedArray pagedArray, int count, Stream stream)
		{
			var binaryFormatter = new BinaryFormatter();
			var buffer = (Array)binaryFormatter.Deserialize(stream);

			foreach (var page in new PageSequence(pagedArray.PageSize, count))
			{
				pagedArray.EnsurePage(page.Index);
				Array.Copy(buffer, page.Offset, pagedArray.GetPage(page.Index), 0, page.Length);
			}
		}
	}
}
