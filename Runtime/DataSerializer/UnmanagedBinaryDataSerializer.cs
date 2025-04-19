using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Massive.Serialization
{
	public class UnmanagedBinaryDataSerializer : IDataSerializer
	{
		public static UnmanagedBinaryDataSerializer Instance { get; } = new UnmanagedBinaryDataSerializer();

		public unsafe void Write(IPagedArray pagedArray, int count, Stream stream)
		{
			var underlyingType = pagedArray.ElementType.IsEnum ? Enum.GetUnderlyingType(pagedArray.ElementType) : pagedArray.ElementType;
			var sizeOfItem = SerializationUtils.SizeOfUnmanaged(underlyingType);

			foreach (var page in new PageSequence(pagedArray.PageSize, count))
			{
				var handle = GCHandle.Alloc(pagedArray.GetPage(page.Index), GCHandleType.Pinned);
				var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), page.Length * sizeOfItem);
				stream.Write(pageAsSpan);
				handle.Free();
			}
		}

		public unsafe void Read(IPagedArray pagedArray, int count, Stream stream)
		{
			var underlyingType = pagedArray.ElementType.IsEnum ? Enum.GetUnderlyingType(pagedArray.ElementType) : pagedArray.ElementType;
			var sizeOfItem = SerializationUtils.SizeOfUnmanaged(underlyingType);

			foreach (var page in new PageSequence(pagedArray.PageSize, count))
			{
				pagedArray.EnsurePage(page.Index);

				var handle = GCHandle.Alloc(pagedArray.GetPage(page.Index), GCHandleType.Pinned);
				var pageAsSpan = new Span<byte>(handle.AddrOfPinnedObject().ToPointer(), page.Length * sizeOfItem);
				stream.Read(pageAsSpan);
				handle.Free();
			}
		}
	}
}
