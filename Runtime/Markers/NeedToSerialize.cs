using System;

namespace Massive.Serialization
{
	/// <summary>
	/// Marks the type to be included in serialization when <see cref="SerializeMode.OnlyMarked"/> mode is active.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate)]
	public class NeedToSerialize : Attribute
	{
	}
}
