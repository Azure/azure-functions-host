using AzureTables;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;

namespace Microsoft.WindowsAzure.Jobs.UnitTests
{
    // Ensure that various "currency" types can be properly serialized and deserialized to AzureTables.
    [TestClass]
    public class TableUnitTests
    {
        // Test that AzureTables class handles enums. (This is significant because the SDK doesn't)
        [TestMethod]
        public void AzureTableClassEnum()
        {
            AzureTable table = AzureTable.NewInMemory();

            table.Clear();

            table.Write("1", "x", new { Fruit = Fruit.Banana });

            // Read normally. Should be serialized as a textual name, not a number
            var x = table.Lookup("1", "x");
            Assert.IsNotNull(x);
            Assert.AreEqual("Banana", x["Fruit"]);

            // Read with strong binder.
            IAzureTableReader<FruitEntity> reader = table.GetTypeSafeWrapper<FruitEntity>();
            FruitEntity f = reader.Lookup("1", "x");
            Assert.AreEqual(Fruit.Banana, f.Fruit);
        }

        class FruitEntity
        {
            public Fruit Fruit { get; set; }
        }

        public enum Fruit
        {
            Apple,
            Banana,
            Pear,
        }
    }
}