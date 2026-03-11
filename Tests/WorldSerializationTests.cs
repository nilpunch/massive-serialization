using System.IO;
using NUnit.Framework;

namespace Massive.Serialization.Tests
{
	[TestFixture]
	public class WorldSerializationTests
	{
		public struct TestComp : IAuto<TestComp>
		{
			public ArrayPointer<int> Ptr;
		}

		[Test]
		public void Deserialization_ShouldNotBreakDefaultValues()
		{
			var world = new World();
			var worldSerializer = new WorldSerializer();
			var buffer = new MemoryStream();

			// Create entities.
			for (int i = 0; i < Constants.PageSize * 2; i++)
			{
				world.Create(new TestComp() { Ptr = world.AllocArray<int>(1) });
			}
			world.Include<TestComp>().ForEach((Entity entity) =>
			{
				if (entity.Id % 2 == 0)
				{
					entity.Remove<TestComp>();
				}
			});

			// Serialize.
			worldSerializer.Serialize(world, buffer);

			// Create more.
			for (int i = 0; i < Constants.PageSize; i++)
			{
				world.Create(new TestComp() { Ptr = world.AllocArray<int>(1) });
			}

			// Deserialize.
			buffer.Position = 0;
			worldSerializer.Deserialize(world, buffer);

			// Add more and test.
			for (int i = 0; i < Constants.PageSize; i++)
			{
				var entity = world.CreateEntity();

				entity.Add<TestComp>();

				Assert.AreEqual(default(int), entity.Get<TestComp>().Ptr.Model.Raw.AsInt);
			}
		}
	}
}
