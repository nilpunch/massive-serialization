using System;

namespace Massive.Serialization
{
	/// <summary>
	/// Marks the type to be excluded from serialization when <see cref="SerializeMode.AllExceptMarked"/> mode is active.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate)]
	public class DoNotSerialize : Attribute
	{
	}
}
