using System.IO;

namespace Massive.Serialization
{
	public interface IDataSerializer
	{
		void Write(IDataSet dataSet, BitSet bitSet, Stream stream);
		void Read(IDataSet dataSet, BitSet bitSet, Stream stream);
	}
}
