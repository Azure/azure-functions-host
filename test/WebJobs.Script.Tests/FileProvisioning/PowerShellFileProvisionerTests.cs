// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.FileAugmentation
{
    public class PowerShellFileProvisionerTests
    {
        private const string ProfilePs1FileName = "profile.ps1";
        private const string RequirementsPsd1FileName = "requirements.psd1";

        private readonly string _scriptRootPath;
        private TestLogger _logger;

        public PowerShellFileProvisionerTests()
        {
            _logger = new TestLogger("PowerShellFileProvisionerTests");
            _scriptRootPath = Path.GetTempPath();
        }

        [Fact]
        public async Task AugmentFiles_Test()
        {
            EnsurePowerShellFilesDoNotExist(_scriptRootPath);

            var powerShellFileProvisioner = new PowerShellFileProvisionerMocksPSGalleryCalls(_logger);
            await powerShellFileProvisioner.ProvisionFiles(_scriptRootPath);

            File.Exists(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));
            File.Exists(Path.Combine(_scriptRootPath, ProfilePs1FileName));

            string requirementsContent = File.ReadAllText(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));

            string expectedContent = @"# This file enables modules to be automatically managed by the Functions service.
# Only the Azure Az module is supported in public preview.
# See https://aka.ms/functionsmanageddependency for additional information.
#
@{
    # For latest supported version, go to 'https://www.powershellgallery.com/packages/Az'. 
    'Az' = '2.*'
}";
            Assert.Equal(requirementsContent, expectedContent, StringComparer.OrdinalIgnoreCase);

            ValidateLogs(_logger);
        }

        [Fact]
        public async Task AugmentFiles_UnableToReachThePSGallery_Test()
        {
            EnsurePowerShellFilesDoNotExist(_scriptRootPath);

            var powerShellFileProvisioner = new PowerShellFileProvisionerMocksPSGalleryCalls(_logger);
            powerShellFileProvisioner.GetLatestAzModuleMajorVersionThrowsException = true;
            await powerShellFileProvisioner.ProvisionFiles(_scriptRootPath);

            File.Exists(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));
            File.Exists(Path.Combine(_scriptRootPath, ProfilePs1FileName));

            string requirementsContent = File.ReadAllText(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));

            string expectedContent = @"# This file enables modules to be automatically managed by the Functions service.
# Only the Azure Az module is supported in public preview.
# See https://aka.ms/functionsmanageddependency for additional information.
#
@{
    # For latest supported version, go to 'https://www.powershellgallery.com/packages/Az'. Uncomment the next line and replace the MAJOR_VERSION, e.g., 'Az' = '2.*'
    # 'Az' = 'MAJOR_VERSION.*'
}";
            Assert.Equal(requirementsContent, expectedContent, StringComparer.OrdinalIgnoreCase);

            ValidateLogs(_logger, unableToReachPSGallery: true);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AugmentFiles_EmptyScriptRootPath_Test(string scriptRootPath)
        {
            var powerShellFileProvisioner = new PowerShellFileProvisioner(_logger);
            Exception ex = await Assert.ThrowsAsync<ArgumentException>(async () => await powerShellFileProvisioner.ProvisionFiles(scriptRootPath));
            Assert.True(ex is ArgumentException);
        }

        private void EnsurePowerShellFilesDoNotExist(string functionAppRootPath)
        {
            File.Delete(Path.Combine(functionAppRootPath, RequirementsPsd1FileName));
            File.Delete(Path.Combine(functionAppRootPath, ProfilePs1FileName));
        }

        private void ValidateLogs(TestLogger logger, bool unableToReachPSGallery = false)
        {
            List<string> expectedLogs = new List<string>();
            expectedLogs.Add($"Creating {RequirementsPsd1FileName}.");
            expectedLogs.Add($"{RequirementsPsd1FileName} created sucessfully.");
            expectedLogs.Add($"Creating {ProfilePs1FileName}.");
            expectedLogs.Add($"{ProfilePs1FileName} created sucessfully.");

            if (unableToReachPSGallery)
            {
                expectedLogs.Add("Failed to get Az module version. Edit the requirements.psd1 file when the powershellgallery.com is accessible.");
            }

            var logs = logger.GetLogMessages();
            foreach (string log in expectedLogs)
            {
                Assert.True(logs.Any(l => string.Equals(l.FormattedMessage, log)));
            }
        }
    }

    internal class PowerShellFileProvisionerMocksPSGalleryCalls : PowerShellFileProvisioner
    {
        internal PowerShellFileProvisionerMocksPSGalleryCalls(ILogger logger) : base(logger) { }

        public bool GetLatestAzModuleMajorVersionThrowsException { get; set; }

        protected override string GetLatestAzModuleMajorVersion()
        {
            if (GetLatestAzModuleMajorVersionThrowsException)
            {
                throw new Exception($@"Fail to get module version for 'Az'.");
            }

            return "2";
        }
    }
}
