using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class PublicSurfaceTests
    {
        [Fact]
        public void AssemblyReferences_InJobsAssembly()
        {
            // The DLL containing the binding attributes should be truly minimal and have no extra dependencies. 
            var names = GetAssemblyReferences(typeof(BlobInputAttribute).Assembly);

            Assert.Equal(1, names.Count);
            Assert.Equal("mscorlib", names[0]);
        }

        [Fact]
        public void AssemblyReferences_InJobsHostAssembly()
        {
            var names = GetAssemblyReferences(typeof(JobHost).Assembly);

            foreach (var name in names)
            {
                if (name.StartsWith("Microsoft.WindowsAzure"))
                {
                    // Only azure dependency is on the storage sdk
                    Assert.Equal("Microsoft.WindowsAzure.Storage", name);
                }
            }
        }

        [Fact]
        public void JobsPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(QueueTriggerAttribute).Assembly;

            var expected = new[] {
                "ServiceBusAttribute",
                "BlobInputAttribute",
                "BlobOutputAttribute",
                "DescriptionAttribute",
                "QueueTriggerAttribute",
                "QueueOutputAttribute",
                "NoAutomaticTriggerAttribute",
                "TableAttribute",
                "IBinder",
                "ICloudBlobStreamBinder`1",
            };

            AssertPublicTypes(expected, assembly);
        }

        [Fact]
        public void JobsHostPublicSurface_LimitedToSpecificTypes()
        {
            var assembly = typeof(Microsoft.Azure.Jobs.JobHost).Assembly;

            var expected = new[] { 
                "JobHost", 
                "JobHostConfiguration", 
                "ITypeLocator", 
                "INameResolver", 
                "WebJobsShutdownWatcher" 
            };

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
                string message = String.Format("Found {0} unexpected public type{1}: \r\n{2}",
                    newlyIntroducedPublicTypes.Length,
                    newlyIntroducedPublicTypes.Length == 1 ? "" : "s",
                    string.Join("\r\n", newlyIntroducedPublicTypes));
                Assert.True(false, message);
            }

            var missingPublicTypes = expected.Except(actual).ToArray();

            if (missingPublicTypes.Length > 0)
            {
                string message = String.Format("missing {0} public type{1}: \r\n{2}",
                    missingPublicTypes.Length,
                    missingPublicTypes.Length == 1 ? "" : "s",
                    string.Join("\r\n", missingPublicTypes));
                Assert.True(false, message);
            }
        }
    }
}
