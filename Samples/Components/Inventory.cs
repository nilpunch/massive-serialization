using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Massive.Samples.Serialization
{
	[DataContract]
	public struct Inventory
	{
		[DataMember]
		public List<int> Items;
	}
}
