// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PackageManagerTests
    {
        private static readonly ScriptSettingsManager SettingsManager = ScriptSettingsManager.Instance;

        [Theory]
        [InlineData(@"ProjectWithLockMatch", false)]
        [InlineData(@"FunctionWithNoProject", false)]
        [InlineData(@"ProjectWithMismatchedLock\MismatchedGroupFrameworkDependencies", true)]
        [InlineData(@"ProjectWithMismatchedLock\MismatchedPackageVersions", true)]
        [InlineData(@"ProjectWithMismatchedLock\MismatchedProjectDependencies", true)]
        [InlineData(@"ProjectWithoutLock", true)]
        public void RequirePackageRestore_ReturnsExpectedResult(string projectPath, bool shouldRequireRestore)
        {
            projectPath = Path.Combine(Directory.GetCurrentDirectory(), @"Description\DotNet\TestFiles\PackageReferences", projectPath);
            bool result = PackageManager.RequiresPackageRestore(projectPath);

            Assert.True(Directory.Exists(projectPath));

            string message = $"Project in '{projectPath}' did not return expected result.";

            // Using .True or .False (instead of .Equal) to trace additional information.
            if (shouldRequireRestore)
            {
                Assert.True(result, message);
            }
            else
            {
                Assert.False(result, message);
            }
        }

        [Fact]
        public void ResolveNuGetPath_WithNoEnvironmentHint_AndNoLocalFile_ReturnsExpectedResult()
        {
            using (var variables = new TestScopedSettings(SettingsManager, "AzureWebJobs_NuGetPath", null))
            {
                string result = PackageManager.ResolveNuGetPath();

                Assert.Equal("nuget.exe", result);
            }
        }

        [Fact]
        public void ResolveNuGetPath_Local_WithNoEnvironmentHint_ReturnsExpectedResult()
        {
            using (var variables = new TestScopedSettings(SettingsManager, "AzureWebJobs_NuGetPath", null))
            using (var nugetDirectory = new TempDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin\\tools\\")))
            {
                string nugetPath = Path.Combine(nugetDirectory.Path, "nuget.exe");
                File.WriteAllText(nugetPath, string.Empty);

                string result = PackageManager.ResolveNuGetPath();

                Assert.Equal(nugetPath, result);
            }
        }

        [Fact]
        public void ResolveNuGetPath_Local_WithEnvironmentHint_ReturnsExpectedResult()
        {
            string path = @"c:\some\path\to\nuget.exe";

            using (var variables = new TestScopedSettings(SettingsManager, "AzureWebJobs_NuGetPath", path))
            {
                string result = PackageManager.ResolveNuGetPath();

                Assert.Equal(path, result);
            }
        }
    }
}
