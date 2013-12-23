using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;

namespace Microsoft.WindowsAzure.Jobs.UnitTests
{
    [TestClass]
    public class PublicSurfaceTests
    {
        [TestMethod]
        public void AssemblyReferencesMinDll()
        {            
            // The DLL containing the binding attributes should be truly minimal and have no extra dependencies. 
            var assembly = typeof(BlobInputAttribute).Assembly;
            var assemblyRefs = assembly.GetReferencedAssemblies();
            var names = Array.ConvertAll(assemblyRefs, x => x.Name);
            Array.Sort(names);

            Assert.AreEqual(2, names.Length);
            Assert.AreEqual("mscorlib", names[0]);
            Assert.AreEqual("System.Core", names[1]);
        }

        [TestMethod]
        public void AssemblyReferencesHost()
        {
            var assembly = typeof(Host).Assembly;
            var assemblyRefs = assembly.GetReferencedAssemblies();
            var names = Array.ConvertAll(assemblyRefs, x => x.Name);
            
            // The Azure SDK is brittle and has breaking changes. 
            // We just depend on 1.7 and avoid a direct dependency on 2x or later. 
            foreach (var name in names)
            {
                if (name.Equals("Microsoft.WindowsAzure.Jobs"))
                {
                    continue;
                }
                if (name.StartsWith("Microsoft.WindowsAzure"))
                {             
                    Assert.AreEqual("Microsoft.WindowsAzure.StorageClient", name, "Only azure dependency is on the 1.7 sdk");
                }
            }            
        }

        [TestMethod]
        public void JobsPublicSurface()
        {
            var assembly = typeof(QueueInputAttribute).Assembly;

            var expected = new[] {
                "BlobInputAttribute",
                "BlobInputsAttribute",
                "BlobOutputAttribute",
                "DescriptionAttribute",
                "QueueInputAttribute",
                "QueueOutputAttribute",
                "NoAutomaticTriggerAttribute",
                "TableAttribute",
                "IBinder",
                "ICloudBlobStreamBinder`1",
            };

            AssertPublicTypes(expected, assembly);
        }

        [TestMethod]
        public void JobsHostPublicSurface()
        {
            var assembly = typeof(Microsoft.WindowsAzure.Jobs.Host).Assembly;

            var expected = new[] { "Host" };

            AssertPublicTypes(expected, assembly);
        }

        static void AssertPublicTypes(IEnumerable<string> expected, Assembly assembly)
        {
            var publicTypes = (assembly.GetExportedTypes()
                .Select(type => type.Name)
                .OrderBy(n => n));

            AssertPublicTypes(expected.ToArray(), publicTypes.ToArray());
        }

        static void AssertPublicTypes(string[] expected, string[] actual)
        {
            var newlyIntroducedPublicTypes = actual.Except(expected).ToArray();

            if (newlyIntroducedPublicTypes.Length > 0)
            {
                Assert.Fail("Found {0} unexpected public type{1}: \r\n{2}",
                    newlyIntroducedPublicTypes.Length,
                    newlyIntroducedPublicTypes.Length == 1 ? "" : "s",
                    string.Join("\r\n", newlyIntroducedPublicTypes));
            }

            var missingPublicTypes = expected.Except(actual).ToArray();

            if (missingPublicTypes.Length > 0)
            {
                Assert.Fail("missing {0} public type{1}: \r\n{2}",
                    missingPublicTypes.Length,
                    missingPublicTypes.Length == 1 ? "" : "s",
                    string.Join("\r\n", missingPublicTypes));
            }
        }
    }
}
