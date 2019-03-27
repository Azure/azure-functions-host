// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.FileAugmentation;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.FileAugmentation
{
    public class AppFileAugmentationServiceTests
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _optionsMonitor;
        private readonly IFuncAppFileAugmentorFactory _funcAppFileAugmentorFactory;
        private readonly IEnvironment _environment;
        private readonly string _scriptRootPath;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public AppFileAugmentationServiceTests()
        {
            _scriptRootPath = Path.GetTempPath();
            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                ScriptPath = _scriptRootPath
            };

            _optionsMonitor = TestHelpers.CreateOptionsMonitor(applicationHostOptions);
            _environment = new TestEnvironment();
            _funcAppFileAugmentorFactory = new FuncAppFileAugmentorFactory();
        }

        [Fact]
        public async Task Readonly_FunAppRoot_Test()
        {
            File.Delete(Path.Combine(_scriptRootPath, "host.json"));
            File.Delete(Path.Combine(_scriptRootPath, "requirements.psd1"));
            File.Delete(Path.Combine(_scriptRootPath, "profile.ps1"));
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, "1");
            var appFileAugmentationService = new AppFileAugmentationService(_environment, _optionsMonitor, _funcAppFileAugmentorFactory);
            await appFileAugmentationService.StartAsync(_cancellationTokenSource.Token);
            Assert.True(!File.Exists(Path.Combine(_scriptRootPath, "host.json")));
            Assert.True(!File.Exists(Path.Combine(_scriptRootPath, "requirements.psd1")));
            Assert.True(!File.Exists(Path.Combine(_scriptRootPath, "profile.ps1")));
        }

        [Theory]
        [InlineData("dotnet")]
        [InlineData("powershell")]
        public async Task Create_App_Files_NonPowershell_Runtime_Test(string workerRuntime)
        {
            File.Delete(Path.Combine(_scriptRootPath, "host.json"));
            File.Delete(Path.Combine(_scriptRootPath, "requirements.psd1"));
            File.Delete(Path.Combine(_scriptRootPath, "profile.ps1"));
            _environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            var appFileAugmentationService = new AppFileAugmentationService(_environment, _optionsMonitor, _funcAppFileAugmentorFactory);
            await appFileAugmentationService.StartAsync(_cancellationTokenSource.Token);
            if (string.Equals(workerRuntime, "powershell", StringComparison.InvariantCultureIgnoreCase))
            {
                Assert.True(File.Exists(Path.Combine(_scriptRootPath, "host.json")));
                Assert.True(File.Exists(Path.Combine(_scriptRootPath, "requirements.psd1")));
                Assert.True(File.Exists(Path.Combine(_scriptRootPath, "profile.ps1")));
            }
            else
            {
                Assert.True(!File.Exists(Path.Combine(_scriptRootPath, "host.json")));
                Assert.True(!File.Exists(Path.Combine(_scriptRootPath, "requirements.psd1")));
                Assert.True(!File.Exists(Path.Combine(_scriptRootPath, "profile.ps1")));
            }
        }
    }
}
