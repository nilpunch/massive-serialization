using System.IO;

namespace Massive.Serialization
{
	public interface IDataSerializer
	{
		void Write(IDataSet dataSet, Stream stream);
		void Read(IDataSet dataSet, Stream stream);
	}
}
