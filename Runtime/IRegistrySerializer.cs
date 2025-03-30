using System.IO;

namespace Massive.Serialization
{
	public interface IRegistrySerializer
	{
		void Serialize(World world, Stream stream);
		void Deserialize(World world, Stream stream);
	}
}
