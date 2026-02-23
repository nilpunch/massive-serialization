using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Massive.Serialization
{
	public class AQNTypeSerializer : ITypeSerializer
	{
		public static AQNTypeSerializer Instance { get; } = new AQNTypeSerializer();

		private readonly Dictionary<Type, byte[]> _typeToBytes = new Dictionary<Type, byte[]>();
		private readonly Dictionary<ulong, Type> _hashToType = new Dictionary<ulong, Type>();
		private readonly Dictionary<Type, ulong> _typeToHash = new Dictionary<Type, ulong>();

		public void Serialize(Type type, Stream stream)
		{
			if (!_typeToBytes.TryGetValue(type, out var nameBytes))
			{
				var typeName = type.AssemblyQualifiedName!;
				nameBytes = Encoding.UTF8.GetBytes(typeName);
				var hash = GetStableHash(typeName);
				_typeToBytes[type] = nameBytes;
				_hashToType[hash] = type;
				_typeToHash[type] = hash;
			}

			stream.WriteULong(_typeToHash[type]);
			stream.WriteInt(nameBytes.Length);
			stream.Write(nameBytes);
		}

		public Type Deserialize(Stream stream)
		{
			var hash = stream.ReadULong();
			var nameLength = stream.ReadInt();
			var nameBytes = ArrayPool<byte>.Shared.Rent(nameLength);
			try
			{
				stream.ReadExactly(new Span<byte>(nameBytes, 0, nameLength));
				if (!_hashToType.TryGetValue(hash, out var type))
				{
					var typeName = Encoding.UTF8.GetString(nameBytes, 0, nameLength);
					type = Type.GetType(typeName, true);
					_typeToBytes[type] = nameBytes.ToArray();
					_hashToType[hash] = type;
					_typeToHash[type] = hash;
				}
				return type;
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(nameBytes);
			}
		}

		private static ulong GetStableHash(string str)
		{
			const ulong fnvOffset = 14695981039346656037;
			const ulong fnvPrime = 1099511628211;
			var hash = fnvOffset;
			foreach (var c in str)
			{
				hash ^= c;
				hash *= fnvPrime;
			}
			return hash;
		}
	}
}
