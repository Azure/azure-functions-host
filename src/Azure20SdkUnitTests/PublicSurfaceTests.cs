using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;

namespace Azure20SdkUnitTests
{
    [TestClass]
    public class PublicSurfaceTests
    {
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
                "TableAttribute",
                "IBinder",
                "ICloudBlobStreamBinder`1",
            };

            AssertPublicTypes(expected, assembly);
        }

        [TestMethod]
        public void JobsHostPublicSurface()
        {
            var assembly = typeof(Host).Assembly;

            var expected = new[] { "Host" };

            AssertPublicTypes(expected, assembly);
        }

        [TestMethod]
        public void Azure20SDKBindersPublicSurface()
        {
            var assembly = typeof(Microsoft.WindowsAzure.Jobs.Azure20SdkBinders.Utility).Assembly;

            var expected = Enumerable.Empty<string>();

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
