using System;
using System.IO;

namespace Massive.Serialization
{
	public interface ITypeSerializer
	{
		void Serialize(Type type, Stream stream);
		Type Deserialize(Stream stream);
	}
}
