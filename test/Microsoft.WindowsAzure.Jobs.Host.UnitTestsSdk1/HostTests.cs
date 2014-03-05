using System;
using System.IO;
using Microsoft.WindowsAzure.Jobs.Host;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    // Unit test the static parameter bindings. This primarily tests the indexer.
    public class HostUnitTests
    {
        [Fact]
        public void TestSdkMarkerIsWrittenWhenInAntares()
        {
            string tempDir = Path.GetTempPath();
            const string filename = "WebJobsSdk.marker";

            var path = Path.Combine(tempDir, filename);

            File.Delete(path);


            try
            {
                Environment.SetEnvironmentVariable(WebSitesKnownKeyNames.JobDataPath, tempDir);

                // don't use storage account at all, just testing the manifest file. 
                var hooks = new JobHostTestHooks 
                { 
                    StorageValidator = new NullStorageValidator(),
                    TypeLocator = new SimpleTypeLocator() // No types
                };
                JobHost h = new JobHost(dataConnectionString: null, runtimeConnectionString: null, hooks: hooks);
                Assert.True(File.Exists(path), "SDK marker file should have been written");
            }
            finally
            {
                Environment.SetEnvironmentVariable(WebSitesKnownKeyNames.JobDataPath, null);
                File.Delete(path);
            }
        }

        [Fact]
        public void SimpleInvoke()
        {
            var host = new TestJobHost<ProgramSimple>(null);

            var x = "abc";
            ProgramSimple._value = null;
            host.Call("Test", new { value = x });

            // Ensure test method was invoked properly.
            Assert.Equal(x, ProgramSimple._value);
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
