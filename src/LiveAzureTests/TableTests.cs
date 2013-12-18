using System;
using System.Linq;
using AzureTables;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiveAzureTests
{
    // Azure storage emulator is doesn't support Upsert, so table won't work. 
    // Need to run table tests against live storage
    [TestClass]
    public class TableTests
    {
        // This test takes 2 mins.
        //[TestMethod]
        public void AzureTableClassDelete()
        {
            var tableName = "testTable" + DateTime.Now.Ticks.ToString();
            AzureTable table = new AzureTable(AzureConfig.GetAccount(), tableName);
            try
            {

                int N = 2000; // Pick > 1000 to deal with azure corner cases 
                for (int i = 0; i < N; i++)
                {
                    table.Write("1", i.ToString(), new { val = i * 10 });
                }
                table.Write("2", "x", new { val = 15 });

                // Delete a single row.
                {
                    var x = table.Lookup("1", "90");
                    Assert.IsNotNull(x);
                    Assert.AreEqual("900", x["val"]);

                    table.Delete("1", "90");
                    var x2 = table.Lookup("1", "90");
                    Assert.IsNull(x2, "item should be deleted");
                }

                // Delete the whole partition
                {
                    var all = table.Enumerate("1");
                    int count = all.Count();
                    Assert.AreEqual(N - 1, count);

                    table.Delete("1");  // This could take minutes. 

                    var all2 = table.Enumerate("1");
                    int count2 = all2.Count();
                    Assert.AreEqual(0, count2);

                    // Verify we didn't delete other partitions
                    var x = table.Lookup("2", "x");
                    Assert.IsNotNull(x);
                }
            }
            finally
            {
                table.ClearAsync();
            }
        }
    }
}
