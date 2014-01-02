using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    // Unit test the static parameter bindings. This primarily tests the indexer.
    [TestClass]
    public class HostUnitTests
    {
        [TestMethod]
        public void TestAntaresManifestIsWritten()
        {
            string path = Path.GetTempFileName();

            File.Delete(path);

            const string EnvVar = "JOB_EXTRA_INFO_URL_PATH";
            try
            {
                Environment.SetEnvironmentVariable(EnvVar, path);

                // don't use storage account at all, just testing the manifest file. 
                var hooks = new JobHostTestHooks 
                { 
                    StorageValidator = new NullStorageValidator(),
                    TypeLocator = new SimpleTypeLocator() // No types
                };
                string acs = null;
                JobHost h = new JobHost(acs, acs, hooks);

                Assert.IsTrue(File.Exists(path), "Manifest file should have been written");

                // Validate contents
                string contents = File.ReadAllText(path);
                Assert.AreEqual("/sb", contents);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvVar, null);
                File.Delete(path);
            }
        }

        [TestMethod]
        public void SimpleInvoke()
        {
            Type t = typeof(ProgramSimple);
            JobHost h = CreateInMemoryHost(t);

            var x = "abc";
            ProgramSimple._value = null;
            h.Call(t.GetMethod("Test"), new { value = x });

            Assert.AreEqual(x, ProgramSimple._value, "Test method was not invoked properly.");
        }

        class ProgramSimple
        {
            public static string _value; // evidence of execution

            [Description("empty test function")]
            public static void Test(string value)
            {
                _value = value;
            }
        }

        private static JobHost CreateInMemoryHost(Type programType)
        {
            // Use public surface to do an in-memory call.
            var hooks = new JobHostTestHooks
            {
                StorageValidator = new NullStorageValidator(),
                TypeLocator = new SimpleTypeLocator(programType)
            };

            // Doesn't need a storage account. No logging, doesn't bind to blobs, doesn't listen on azure, etc.
            string accountConnectionString = null;
            return new JobHost(accountConnectionString, accountConnectionString, hooks);
        }
    }
}