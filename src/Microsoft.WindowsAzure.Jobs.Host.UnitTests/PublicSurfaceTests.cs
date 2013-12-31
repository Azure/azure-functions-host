using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.WindowsAzure.Jobs.UnitTests
{
    [TestClass]
    public class PublicSurfaceTests
    {
        [TestMethod]
        public void AssemblyReferences_InJobsAssembly()
        {
            // The DLL containing the binding attributes should be truly minimal and have no extra dependencies. 
            var names = GetAssemblyReferences(typeof(BlobInputAttribute).Assembly);

            Assert.AreEqual(1, names.Count);
            Assert.AreEqual("mscorlib", names[0]);
        }

        [TestMethod]
        public void AssemblyReferences_InJobsHostAssembly()
        {
            var names = GetAssemblyReferences(typeof(JobHost).Assembly);

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
        public void JobsPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(QueueInputAttribute).Assembly;

            var expected = new[] {
                "BlobInputAttribute",
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
        public void JobsHostPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(Microsoft.WindowsAzure.Jobs.JobHost).Assembly;

            var expected = new[] { "JobHost" };

            AssertPublicTypes(expected, assembly);
        }

        private static List<string> GetAssemblyReferences(Assembly assembly)
        {
            var assemblyRefs = assembly.GetReferencedAssemblies();
            var names = (from assemblyRef in assemblyRefs
                         orderby assemblyRef.Name.ToLowerInvariant()
                         select assemblyRef.Name).ToList();
            return names;
        }

        private static void AssertPublicTypes(IEnumerable<string> expected, Assembly assembly)
        {
            var publicTypes = (assembly.GetExportedTypes()
                .Select(type => type.Name)
                .OrderBy(n => n));

            AssertPublicTypes(expected.ToArray(), publicTypes.ToArray());
        }

        private static void AssertPublicTypes(string[] expected, string[] actual)
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
