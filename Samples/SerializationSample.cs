using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Massive.Serialization;

namespace Massive.Samples.Serialization
{
	class SerializationSample
	{
		private static readonly string ApplicationPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
		private static readonly string PathToSaveFile = Path.Combine(ApplicationPath!, "save.txt");

		static void Main()
		{
			var world = new World();

			PopulateEntitiesAndComponents(world);

			var worldSerializer = new WorldSerializer();

			// By default, managed types are serialized using BinaryFormatter, which requires the [Serializable] attribute on the component.
			// Custom serialization can be implemented like this:
			worldSerializer.SetCustomSerializer(typeof(Inventory), DataContractDataSerializer.Instance);

			// Save world to the file.
			using (FileStream stream = new FileStream(PathToSaveFile, FileMode.Create, FileAccess.Write))
			{
				worldSerializer.Serialize(world, stream);
			}

			// Load world from the file.
			// It is important to use a serializer with the same configuration as for saving.
			var savedRegistry = new World();
			using (FileStream stream = new FileStream(PathToSaveFile, FileMode.Open, FileAccess.Read))
			{
				worldSerializer.Deserialize(savedRegistry, stream);
			}

			// Done, use your world as you wish.
		}

		private static void PopulateEntitiesAndComponents(World world)
		{
			for (int i = 0; i < 10; ++i)
			{
				var playerEntity = world.Create<Player>();
				world.Set(playerEntity, new Health() { Value = 5 + i });
				world.Set(playerEntity, new Inventory()
				{
					Items = new List<int>()
					{
						i, 2, 3
					}
				});
			}

			for (int i = 0; i < 5; ++i)
			{
				var enemyEntity = world.Create<Enemy>();
				world.Set(enemyEntity, new Health() { Value = 1 + i });
			}
		}
	}
}
