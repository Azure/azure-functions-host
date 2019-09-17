// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.FileAugmentation
{
    public class PowerShellFileProvisionerTests
    {
        private const string ProfilePs1FileName = "profile.ps1";
        private const string RequirementsPsd1FileName = "requirements.psd1";

        private const string RequirementsPsd1PSGalleryOnlineResourceFileName =
            "Microsoft.Azure.WebJobs.Script.Tests.Resources.FileProvisioning.PowerShell.requirements_PSGalleryOnline.psd1";

        private const string RequirementsPsd1PSGalleryOfflineResourceFileName =
            "Microsoft.Azure.WebJobs.Script.Tests.Resources.FileProvisioning.PowerShell.requirements_PSGalleryOffline.psd1";

        private const string PSGallerySampleFeedResourceFileName =
            "Microsoft.Azure.WebJobs.Script.Tests.Resources.FileProvisioning.PowerShell.PSGallerySampleFeed.xml";

        private const string PSGalleryEmptyFeedResourceFileName =
            "Microsoft.Azure.WebJobs.Script.Tests.Resources.FileProvisioning.PowerShell.PSGalleryEmptyFeed.xml";

        private readonly string _scriptRootPath;
        private readonly ILoggerFactory _loggerFactory = new LoggerFactory();
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        public PowerShellFileProvisionerTests()
        {
            _scriptRootPath = Path.GetTempPath();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_loggerProvider);
        }

        [Fact]
        public async Task AugmentFiles_Test()
        {
            EnsurePowerShellFilesDoNotExist(_scriptRootPath);

            var powerShellFileProvisioner = new TestPowerShellFileProvisioner(_loggerFactory);
            await powerShellFileProvisioner.ProvisionFiles(_scriptRootPath);

            File.Exists(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));
            File.Exists(Path.Combine(_scriptRootPath, ProfilePs1FileName));

            string requirementsContent = File.ReadAllText(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));
            string expectedContent = FileUtility.ReadResourceString(RequirementsPsd1PSGalleryOnlineResourceFileName);
            Assert.Equal(expectedContent, requirementsContent, StringComparer.OrdinalIgnoreCase);

            ValidateLogs(_loggerProvider, _scriptRootPath);
        }

        [Fact]
        public async Task AugmentFiles_UnableToReachThePSGallery_Test()
        {
            EnsurePowerShellFilesDoNotExist(_scriptRootPath);

            var powerShellFileProvisioner = new TestPowerShellFileProvisioner(_loggerFactory)
            {
                GetLatestAzModuleMajorVersionThrowsException = true
            };
            await powerShellFileProvisioner.ProvisionFiles(_scriptRootPath);

            File.Exists(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));
            File.Exists(Path.Combine(_scriptRootPath, ProfilePs1FileName));

            string requirementsContent = File.ReadAllText(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));
            string expectedContent = FileUtility.ReadResourceString(RequirementsPsd1PSGalleryOfflineResourceFileName);
            Assert.Equal(expectedContent, requirementsContent, StringComparer.OrdinalIgnoreCase);

            ValidateLogs(_loggerProvider, _scriptRootPath, unableToReachPSGallery: true);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AugmentFiles_EmptyScriptRootPath_Test(string scriptRootPath)
        {
            var powerShellFileProvisioner = new PowerShellFileProvisioner(_loggerFactory);
            Exception ex = await Assert.ThrowsAsync<ArgumentException>(async () => await powerShellFileProvisioner.ProvisionFiles(scriptRootPath));
            Assert.True(ex is ArgumentException);
        }

        [Fact]
        public void FindLatestMajorVersionTest()
        {
            var powerShellFileProvisioner = new PowerShellFileProvisioner(_loggerFactory);

            string streamContent = FileUtility.ReadResourceString(PSGallerySampleFeedResourceFileName);
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(streamContent)))
            {
                string version = powerShellFileProvisioner.GetModuleMajorVersion(stream);
                Assert.Equal("7", version);
            }
        }

        [Fact]
        public void ReturnNullIfVersionNotFoundTest()
        {
            var powerShellFileProvisioner = new PowerShellFileProvisioner(_loggerFactory);

            string streamContent = FileUtility.ReadResourceString(PSGalleryEmptyFeedResourceFileName);
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(streamContent)))
            {
                string version = powerShellFileProvisioner.GetModuleMajorVersion(stream);
                Assert.Equal(null, version);
            }
        }

        private void EnsurePowerShellFilesDoNotExist(string functionAppRootPath)
        {
            File.Delete(Path.Combine(functionAppRootPath, RequirementsPsd1FileName));
            File.Delete(Path.Combine(functionAppRootPath, ProfilePs1FileName));
        }

        private void ValidateLogs(TestLoggerProvider loggerProvider, string functionAppRoot, bool unableToReachPSGallery = false)
        {
            List<string> expectedLogs = new List<string>();
            expectedLogs.Add($"Creating {RequirementsPsd1FileName} at {functionAppRoot}");
            expectedLogs.Add($"{RequirementsPsd1FileName} created sucessfully.");
            expectedLogs.Add($"Creating {ProfilePs1FileName} at {functionAppRoot}");
            expectedLogs.Add($"{ProfilePs1FileName} created sucessfully.");

            if (unableToReachPSGallery)
            {
                expectedLogs.Add("Failed to get Az module version. Edit the requirements.psd1 file when the powershellgallery.com is accessible.");
            }

            var logs = loggerProvider.GetAllLogMessages();
            foreach (string log in expectedLogs)
            {
                Assert.True(logs.Any(l => string.Equals(l.FormattedMessage, log)));
            }
        }
    }
}
