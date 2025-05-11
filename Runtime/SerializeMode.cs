namespace Massive.Serialization
{
	/// <summary>
	/// Defines how types are selected for serialization based on attribute annotations.
	/// </summary>
	public enum SerializeMode
	{
		/// <summary>
		/// Serializes all types, except those marked with <see cref="DoNotSerialize"/>.
		/// </summary>
		AllExceptMarked,

		/// <summary>
		/// Serializes only types explicitly marked with <see cref="NeedToSerialize"/>.
		/// </summary>
		OnlyMarked,
	}
}
