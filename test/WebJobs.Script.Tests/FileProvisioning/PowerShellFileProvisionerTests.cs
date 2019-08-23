// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Common.Constants;
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

            var powerShellFileProvisioner = new TestPowerShellFileProvisioner(_logger);
            await powerShellFileProvisioner.ProvisionFiles(_scriptRootPath);

            File.Exists(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));
            File.Exists(Path.Combine(_scriptRootPath, ProfilePs1FileName));

            string requirementsContent = File.ReadAllText(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));

            const string ExpectedContent = @"# This file enables modules to be automatically managed by the Functions service.
# See https://aka.ms/functionsmanageddependency for additional information.
#
@{
    # For latest supported version, go to 'https://www.powershellgallery.com/packages/Az'. 
    'Az' = '2.*'
}";
            Assert.Equal(ExpectedContent, requirementsContent, StringComparer.OrdinalIgnoreCase);

            ValidateLogs(_logger);
        }

        [Fact]
        public async Task AugmentFiles_UnableToReachThePSGallery_Test()
        {
            EnsurePowerShellFilesDoNotExist(_scriptRootPath);

            var powerShellFileProvisioner = new TestPowerShellFileProvisioner(_logger);
            powerShellFileProvisioner.GetLatestAzModuleMajorVersionThrowsException = true;
            await powerShellFileProvisioner.ProvisionFiles(_scriptRootPath);

            File.Exists(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));
            File.Exists(Path.Combine(_scriptRootPath, ProfilePs1FileName));

            string requirementsContent = File.ReadAllText(Path.Combine(_scriptRootPath, RequirementsPsd1FileName));

            const string ExpectedContent = @"# This file enables modules to be automatically managed by the Functions service.
# See https://aka.ms/functionsmanageddependency for additional information.
#
@{
    # For latest supported version, go to 'https://www.powershellgallery.com/packages/Az'. Uncomment the next line and replace the MAJOR_VERSION, e.g., 'Az' = '2.*'
    # 'Az' = 'MAJOR_VERSION.*'
}";
            Assert.Equal(ExpectedContent, requirementsContent, StringComparer.OrdinalIgnoreCase);

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

        [Fact]
        public void FindLatestMajorVersionTest()
        {
            const string StreamContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<feed
    xml:base=""https://www.powershellgallery.com/api/v2""
    xmlns=""http://www.w3.org/2005/Atom""
    xmlns:d=""http://schemas.microsoft.com/ado/2007/08/dataservices""
    xmlns:m=""http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"">
  <link rel=""self"" href=""https://www.powershellgallery.com/api/v2/Packages"" />
  <entry>
    <m:properties>
      <d:Version>5.1.2.3</d:Version>
      <d:IsPrerelease m:type=""Edm.Boolean"">false</d:IsPrerelease>
    </m:properties>
  </entry>
  <entry>
    <m:properties>
      <d:Version>7.1.2.6</d:Version>
      <d:IsPrerelease m:type=""Edm.Boolean"">false</d:IsPrerelease>
    </m:properties>
  </entry>
  <entry>
    <m:properties>
      <d:Version>3.1.3.5</d:Version>
      <d:IsPrerelease m:type=""Edm.Boolean"">false</d:IsPrerelease>
    </m:properties>
  </entry>
<entry>
    <m:properties>
      <d:Version>1.2.3.5</d:Version>
      <d:IsPrerelease m:type=""Edm.Boolean"">false</d:IsPrerelease>
    </m:properties>
  </entry>
</feed>";

            var powerShellFileProvisioner = new PowerShellFileProvisioner(_logger);

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(StreamContent)))
            {
                string version = powerShellFileProvisioner.GetModuleMajorVersion(stream);
                Assert.Equal("7", version);
            }
        }

        [Fact]
        public void ReturnNullIfVersionNotFoundTest()
        {
            const string StreamContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<feed
    xml:base=""https://www.powershellgallery.com/api/v2""
    xmlns=""http://www.w3.org/2005/Atom""
    xmlns:d=""http://schemas.microsoft.com/ado/2007/08/dataservices""
    xmlns:m=""http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"">
  <link rel=""self"" href=""https://www.powershellgallery.com/api/v2/Packages"" />
</feed>";

            var powerShellFileProvisioner = new PowerShellFileProvisioner(_logger);

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(StreamContent)))
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
}
