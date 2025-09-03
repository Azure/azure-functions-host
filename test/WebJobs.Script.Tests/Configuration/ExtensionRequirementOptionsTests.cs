// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ExtensionRequirementOptionsTests
    {
        [Fact]
        public void BundleAndExtensionRequired_ReturnsValidConfiguration()
        {
            ExtensionRequirementOptions options = GetOptions("FunctionsHostingEnvironmentConfig.json");
            Assert.Equal(options.Bundles.ElementAt(0).Id, "Microsoft.Azure.Functions.ExtensionBundle");
            Assert.Equal(options.Bundles.ElementAt(0).MinimumVersion, "4.12.0");
            Assert.Equal(options.Extensions.ElementAt(0).PackageName, "Microsoft.Azure.DurableTask.Netherite.AzureFunctions");
        }

        [Fact]
        public void FileNotPresent_ReturnsNull()
        {
            ExtensionRequirementOptions options = GetOptions("FileDoesNotExist.json");
            Assert.Null(options.Bundles);
            Assert.Null(options.Extensions);
        }

        [Fact]
        public void OnlyBundleRequired_ReturnsBundleConfig()
        {
            ExtensionRequirementOptions options = GetOptions("FunctionsHostingEnvironmentConfig_bundlesOnly.json");
            Assert.Equal(options.Bundles.ElementAt(0).Id, "Microsoft.Azure.Functions.ExtensionBundle");
            Assert.Equal(options.Bundles.ElementAt(0).MinimumVersion, "4.12.0");
            Assert.Null(options.Extensions);
        }

        [Fact]
        public void EmptyJsonFile_ReturnsEmptyOptions()
        {
            ExtensionRequirementOptions options = GetOptions("EmptyFile.json");
            Assert.Null(options.Bundles);
            Assert.Null(options.Extensions);
        }

        [Fact]
        public void OnlyExtensionsRequired_ReturnsExtensionConfig()
        {
            ExtensionRequirementOptions options = GetOptions("FunctionsHostingEnvironmentConfig_extensionsOnly.json");
            Assert.Null(options.Bundles);
            Assert.Equal(options.Extensions.ElementAt(0).PackageName, "Microsoft.Azure.DurableTask.Netherite.AzureFunctions");
        }

        private static ExtensionRequirementOptions GetOptions(string fileName)
        {
            string root = Path.GetDirectoryName(typeof(ExtensionRequirementOptionsTests).Assembly.Location);
            string filePath = Path.Combine(root, "TestFixture", "ExtensionRequirementOptionsTests", fileName);
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(filePath, optional: true, reloadOnChange: false)
                .Build();

            ExtensionRequirementOptions options = new();
            ExtensionRequirementOptionsSetup setup = new(configuration);
            setup.Configure(options);
            return options;
        }
    }
}
