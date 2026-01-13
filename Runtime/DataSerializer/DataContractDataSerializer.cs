using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Massive.Serialization
{
	public class DataContractDataSerializer : IDataSerializer
	{
		public static DataContractDataSerializer Instance { get; } = new DataContractDataSerializer();

		private static readonly Dictionary<Type, DataContractSerializer> _serializers = new Dictionary<Type, DataContractSerializer>();
		private static readonly MemoryStream _memoryStream = new MemoryStream(64 * 1024);

		public void Write(IDataSet dataSet, Stream stream)
		{
			var serializer = GetSerializer(dataSet.ArrayType);
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

			var memoryStream = GetMemoryStream();
			using (var writer = XmlDictionaryWriter.CreateBinaryWriter(memoryStream, null, null, ownsStream: false))
			{
				serializer.WriteObject(writer, buffer);
				writer.Flush();
			}

			using var binaryWriter = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
			var length = (int)memoryStream.Length;
			binaryWriter.Write(length);
			binaryWriter.Write(memoryStream.GetBuffer(), 0, length);
		}

		public void Read(IDataSet dataSet, Stream stream)
		{
			var serializer = GetSerializer(dataSet.ArrayType);

			using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
			var length = br.ReadInt32();
			var payload = br.ReadBytes(length);

			var memoryStream = new MemoryStream(payload);
			memoryStream.Position = 0;
			using var reader = XmlDictionaryReader.CreateBinaryReader(memoryStream, XmlDictionaryReaderQuotas.Max);

			var buffer = (Array)serializer.ReadObject(reader);
			var count = 0;

			foreach (var pageIndex in dataSet.GetDataPages())
			{
				dataSet.EnsurePage(pageIndex);

				Array.Copy(buffer, Constants.PageSize * count++, dataSet.GetPage(pageIndex), 0, Constants.PageSize);
			}
		}

		private static MemoryStream GetMemoryStream()
		{
			_memoryStream.Position = 0;
			_memoryStream.SetLength(0);
			return _memoryStream;
		}

		private static DataContractSerializer GetSerializer(Type type)
		{
			if (!_serializers.TryGetValue(type, out var serializer))
			{
				serializer = new DataContractSerializer(type);
				_serializers.Add(type, serializer);
			}
			return serializer;
		}
	}
}
