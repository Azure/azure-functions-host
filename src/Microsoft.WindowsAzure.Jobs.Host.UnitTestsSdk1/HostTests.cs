using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Test;

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
                JobHost h = new JobHost(dataConnectionString: null, runtimeConnectionString: null, hooks: hooks);
                Assert.IsTrue(File.Exists(path), "Manifest file should have been written");

                // Validate contents
                string contents = File.ReadAllText(path);
                Assert.AreEqual("/azurejobs", contents);
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
            var host = new TestJobHost<ProgramSimple>(null);

            var x = "abc";
            ProgramSimple._value = null;
            host.Call("Test", new { value = x });

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
    }
}
