using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Massive.Serialization
{
	public class UnmanagedBinaryDataSerializer : IDataSerializer
	{
		public static UnmanagedBinaryDataSerializer Instance { get; } = new UnmanagedBinaryDataSerializer();

		public unsafe void Write(IDataSet dataSet, Stream stream)
		{
			var underlyingType = dataSet.ElementType.IsEnum ? Enum.GetUnderlyingType(dataSet.ElementType) : dataSet.ElementType;
			var sizeOfItem = ReflectionUtils.SizeOfUnmanaged(underlyingType);

			foreach (var pageIndex in dataSet.GetDataPages())
			{
				var handle = GCHandle.Alloc(dataSet.GetPage(pageIndex), GCHandleType.Pinned);
				var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), Constants.PageSize * sizeOfItem);
				stream.Write(pageAsSpan);
				handle.Free();
			}
		}

		public unsafe void Read(IDataSet dataSet, Stream stream)
		{
			var underlyingType = dataSet.ElementType.IsEnum ? Enum.GetUnderlyingType(dataSet.ElementType) : dataSet.ElementType;
			var sizeOfItem = ReflectionUtils.SizeOfUnmanaged(underlyingType);

			foreach (var pageIndex in dataSet.GetDataPages())
			{
				dataSet.EnsurePage(pageIndex);

				var handle = GCHandle.Alloc(dataSet.GetPage(pageIndex), GCHandleType.Pinned);
				var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), Constants.PageSize * sizeOfItem);
				stream.ReadExactly(pageAsSpan);
				handle.Free();
			}
		}
	}
}
