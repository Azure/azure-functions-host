// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Tests.Properties;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PackageAssemblyResolverTests : IDisposable
    {
        private readonly string _lockFilePath;
        private readonly string _oldHomeEnv;
        private readonly string _runPath;
        private readonly string _targetAssemblyFilePath;
        private readonly string _targetAssemblyPath;

        public PackageAssemblyResolverTests()
        {
            _runPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _lockFilePath = Path.Combine(_runPath, DotNetConstants.ProjectLockFileName);
            _oldHomeEnv = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
            _targetAssemblyPath = Path.Combine(_runPath, "data\\Functions\\packages\\nuget\\Test.Package\\1.0.0\\lib\\net45");
            _targetAssemblyFilePath = Path.Combine(_targetAssemblyPath, Path.GetFileName(this.GetType().Assembly.Location));
            Directory.CreateDirectory(_targetAssemblyPath);

            // Copy current assembly to target package reference location
            File.Copy(this.GetType().Assembly.Location, _targetAssemblyFilePath);

            // Create our Lock file using the current assembly as the target
            File.WriteAllText(_lockFilePath, string.Format(Resources.ProjectLockFileFormatString, Path.GetFileName(this.GetType().Assembly.Location)));

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, _runPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_runPath))
            {
                Directory.Delete(_runPath, true);
            }

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, _oldHomeEnv);
        }

        [Fact]
        public void GivenLockFile_PackageReferencesAreResolved()
        {
            var functionMetadata = new FunctionMetadata()
            {
                Name = "TestFunction",
                ScriptFile = _lockFilePath, /*We just need the path from this*/
                ScriptType = ScriptType.CSharp
            };

            var assemblyResolver = new PackageAssemblyResolver(functionMetadata);

            PackageReference package = assemblyResolver.Packages.Single();

            Assert.Equal("Test.Package", package.Name);
            Assert.Equal("1.0.0", package.Version);
            Assert.Equal(1, package.Assemblies.Count);
            Assert.Equal(2, package.FrameworkAssemblies.Count);
        }

        [Fact]
        public void GivenPackagesWithAssemblyReferences_AssemblyReferencesAreResolved()
        {
            var functionMetadata = new FunctionMetadata()
            {
                Name = "TestFunction",
                ScriptFile = _lockFilePath, /*We just need the path from this*/
                ScriptType = ScriptType.CSharp
            };

            var assemblyResolver = new PackageAssemblyResolver(functionMetadata);

            string nugetHome = PackageManager.GetNugetPackagesPath();

            Assert.Contains<string>(_targetAssemblyFilePath, assemblyResolver.AssemblyReferences);
        }

        [Fact]
        public void GivenPackagesWithFrameworkReferences_FrameworkReferencesAreResolved()
        {
            var functionMetadata = new FunctionMetadata()
            {
                Name = "TestFunction",
                ScriptFile = _lockFilePath, /*We just need the path from this*/
                ScriptType = ScriptType.CSharp
            };

            var assemblyResolver = new PackageAssemblyResolver(functionMetadata);

            Assert.Contains<string>("System.Net.Http", assemblyResolver.AssemblyReferences);
            Assert.Contains<string>("System.Net.Http.WebRequest", assemblyResolver.AssemblyReferences);
        }

        [Fact]
        public void TryResolveAssembly_WithReferencedAssemblyName_ResolvesAssemblyPathAndReturnsTrue()
        {
            var functionMetadata = new FunctionMetadata()
            {
                Name = "TestFunction",
                ScriptFile = _lockFilePath, /*We just need the path from this*/
                ScriptType = ScriptType.CSharp
            };

            var assemblyResolver = new PackageAssemblyResolver(functionMetadata);

            string assemblyPath;
            bool result = assemblyResolver.TryResolveAssembly(this.GetType().Assembly.FullName, out assemblyPath);

            Assert.True(result);
            Assert.Equal(_targetAssemblyFilePath, assemblyPath);
        }

        [Fact]
        public void TryResolveAssembly_WithReferencedFrameworkAssemblyName_ResolvesAssemblyAndReturnsTrue()
        {
            var functionMetadata = new FunctionMetadata()
            {
                Name = "TestFunction",
                ScriptFile = _lockFilePath, /*We just need the path from this*/
                ScriptType = ScriptType.CSharp
            };

            var assemblyResolver = new PackageAssemblyResolver(functionMetadata);

            string assemblyPath;
            bool result = assemblyResolver.TryResolveAssembly("System.Net.Http", out assemblyPath);

            Assert.True(result);
            Assert.Equal("System.Net.Http", assemblyPath);
        }
    }
}
