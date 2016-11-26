// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PowerShellInvokerTests : IClassFixture<PowerShellInvokerTests.Fixture>
    {
        private PowerShellInvokerTests.Fixture _fixture;

        public PowerShellInvokerTests(PowerShellInvokerTests.Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void GetScript()
        {
            string result = PowerShellFunctionInvoker.GetScript(_fixture.TestScriptPath);
            Assert.Equal(_fixture.TestScripContent, result.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetModuleFilePaths()
        {
            List<string> modulesPath = PowerShellFunctionInvoker.GetModuleFilePaths(_fixture.TestRootScriptPath, _fixture.TestFunctionName);
            Assert.Equal(_fixture.TestModulesPath.Count, modulesPath.Count);
            foreach (var modulePath in modulesPath)
            {
                Assert.True(_fixture.TestModulesPath.Contains(modulePath));
            }
        }

        [Fact]
        public void GetRelativePath()
        {
            string functionRootPath = "C:\\";
            string moduleRelativePath = Path.Combine(_fixture.TestFunctionName, "modules\\test-module.psm1");
            string moduleFilePath = Path.Combine(functionRootPath, moduleRelativePath);
            string expectedModuleFilePath = string.Format("/{0}", moduleRelativePath.Replace('\\', '/'));

            string result = PowerShellFunctionInvoker.GetRelativePath(_fixture.TestFunctionName, moduleFilePath);
            Assert.Equal(expectedModuleFilePath, result, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetErrorMessage()
        {
            string scriptFilePath = "C:\\test.ps1";
            ErrorRecord errorRecord = new ErrorRecord(
                new Exception("Object not found."),
                "ObjectNotFound",
                ErrorCategory.ObjectNotFound,
                "TestObject");
            string startMessage = "test.ps1 : Object not found.";

            string result = PowerShellFunctionInvoker.GetErrorMessage(_fixture.TestFunctionName, scriptFilePath, errorRecord);
            Assert.True(result.StartsWith(startMessage));
            Assert.True(result.Contains("+ CategoryInfo          : ObjectNotFound: (TestObject:String) [], Exception"));
            Assert.True(result.Contains("+ FullyQualifiedErrorId : ObjectNotFound"));
        }

        [Fact]
        public void GetStackTrace()
        {
            string scriptFileName = "test.ps1";
            string scriptStackTrace = string.Format(
                @"at Get-DateToday, C:\git\azure-webjobs-sdk-script\sample\{0}\modules\Get-DateToday.psm1: line 4
at <ScriptBlock>, <No file>: line 3", _fixture.TestFunctionName);
            string expectedScriptStackTrace =
                string.Format(@"at Get-DateToday, /{0}/modules/Get-DateToday.psm1: line 4 at {1}: line 3", _fixture.TestFunctionName,
                    scriptFileName);

            string result = PowerShellFunctionInvoker.GetStackTrace(_fixture.TestFunctionName, scriptStackTrace, scriptFileName);
            Assert.Equal(expectedScriptStackTrace, result, StringComparer.OrdinalIgnoreCase);
        }
        
        public class Fixture : IDisposable
        {
            public Fixture()
            {
                TestScripContent = "This is a test script.";
                TestFunctionName = "TestFunction";
                TestRootScriptPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Functions");
                TestFunctionRoot = Path.Combine(TestRootScriptPath, TestFunctionName);
                TestModulesRoot = Path.Combine(TestFunctionRoot, "modules");

                TestScriptPath = CreateScriptFile(TestFunctionRoot);

                TestModules = new string[]
                {
                    "script-module.psm1",
                    "binary-module.dll",
                    "manifest-module.psd1"
                };

                RootTestModules = new string[]
                {
                    "root-script-module.psm1",
                    "root-binary-module.dll",
                    "root-manifest-module.psd1"
                };

                TestModulesPath = CreateModuleFiles(TestModulesRoot, RootTestModules, TestModules);
            }

            public string TestFunctionRoot { get; private set; }

            public string TestRootScriptPath { get; private set; }

            public string TestScriptPath { get; private set; }

            public string TestScripContent { get; private set; }

            public string TestFunctionName { get; private set; }

            public string[] TestModules { get; private set; }

            public string[] RootTestModules { get; private set; }

            public string TestModulesRoot { get; private set; }

            public List<string> TestModulesPath { get; set; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(TestHelpers.FunctionsTestDirectory))
                    {
                        Directory.Delete(TestHelpers.FunctionsTestDirectory, recursive: true);
                    }
                }
                catch
                {
                    // occasionally get file in use errors
                }
            }

            public string CreateScriptFile(string scriptRoot)
            {
                string path = Path.Combine(scriptRoot, "testscript.ps1");
                if (!Directory.Exists(scriptRoot))
                {
                    Directory.CreateDirectory(scriptRoot);
                }

                if (!File.Exists(path))
                {
                    // Create a file to write to.
                    using (StreamWriter sw = File.CreateText(path))
                    {
                        sw.WriteLine(TestScripContent);
                    }
                }

                return path;
            }

            public List<string> CreateModuleFiles(string moduleRoot, string[] rootModules, string[] modules)
            {
                if (!Directory.Exists(moduleRoot))
                {
                    Directory.CreateDirectory(moduleRoot);
                }

                string moduleDirinRoot = TestFunctionRoot + "\\modules";

                if (!Directory.Exists(moduleDirinRoot))
                {
                    Directory.CreateDirectory(moduleDirinRoot);
                }
                
                List<string> modulesPath = new List<string>();
                foreach (var module in modules)
                {
                    string path = Path.Combine(moduleRoot, module);
                    if (!File.Exists(path))
                    {
                        // Create a file to write to.
                        using (StreamWriter sw = File.CreateText(path))
                        {
                            sw.WriteLine(string.Format("This is a {0} file.", module));
                        }
                    }

                    modulesPath.Add(path);
                }

                foreach (var module in rootModules)
                {
                    string path = Path.Combine(moduleDirinRoot, module);
                    if (!File.Exists(path))
                    {
                        // Create a file to write to.
                        using (StreamWriter sw = File.CreateText(path))
                        {
                            sw.WriteLine(string.Format("This is a {0} file.", module));
                        }
                    }

                    modulesPath.Add(path);
                }

                return modulesPath;
            }
        }
    }
}
