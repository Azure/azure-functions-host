// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionMetadataManagerTests
    {
        private const string _expectedErrorMessage = "Unable to determine the primary function script.Make sure atleast one script file is present.Try renaming your entry point script to 'run' or alternatively you can specify the name of the entry point script explicitly by adding a 'scriptFile' property to your function metadata.";
        private ScriptJobHostOptions _scriptJobHostOptions = new ScriptJobHostOptions();
        private Mock<IFunctionMetadataProvider> _mockFunctionMetadataProvider;
        private FunctionMetadataManager _testFunctionMetadataManager;
        private HttpWorkerOptions _defaultHttpWorkerOptions;

        public FunctionMetadataManagerTests()
        {
            _mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample\node");
            _defaultHttpWorkerOptions = new HttpWorkerOptions();
            _scriptJobHostOptions.RootScriptPath = functionsPath;
            _testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                _mockFunctionMetadataProvider.Object, new List<IFunctionProvider>(), new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory());
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void IsScriptFileDetermined_ScriptFile_Emtpy_False(string scriptFile)
        {
            FunctionMetadata functionMetadata = GetTestFunctionMetadata(scriptFile);
            Assert.False(_testFunctionMetadataManager.IsScriptFileDetermined(functionMetadata));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void FunctionMetadataManager_Verify_FunctionErrors(string scriptFile)
        {
            Collection<FunctionMetadata> functionMetadataCollection = new Collection<FunctionMetadata>();
            functionMetadataCollection.Add(GetTestFunctionMetadata(scriptFile));

            ImmutableDictionary<string, ImmutableArray<string>> mockFunctionErrors = new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
            Mock<IFunctionMetadataProvider> mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadata(false)).Returns(functionMetadataCollection.ToImmutableArray());
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors);

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>(), new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory());
            Assert.Empty(testFunctionMetadataManager.LoadFunctionMetadata());

            Assert.True(testFunctionMetadataManager.Errors.Count == 1);
            ImmutableArray<string> functionErrors = testFunctionMetadataManager.Errors["testFunction"];
            Assert.True(functionErrors.Length == 1);
            Assert.Equal(_expectedErrorMessage, functionErrors[0]);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void FunctionMetadataManager_Verify_FunctionErrors_FromFunctionProviders(string scriptFile)
        {
            var functionMetadataCollection = new Collection<FunctionMetadata>();
            var mockFunctionErrors = new Dictionary<string, ImmutableArray<string>>();
            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadata(false)).Returns(new Collection<FunctionMetadata>().ToImmutableArray());
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            functionMetadataCollection.Add(GetTestFunctionMetadata(scriptFile));
            functionMetadataCollection.Add(GetTestFunctionMetadata(scriptFile, name: "anotherFunction"));
            mockFunctionErrors["anotherFunction"] = new List<string>() { "error" }.ToImmutableArray();

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object }, new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory());
            testFunctionMetadataManager.LoadFunctionMetadata();

            Assert.Equal(2, testFunctionMetadataManager.Errors.Count);
            ImmutableArray<string> functionErrors = testFunctionMetadataManager.Errors["anotherFunction"];
            Assert.Equal(2, functionErrors.Length);
            Assert.True(functionErrors.Contains("error"));
        }

        [Fact]
        public void FunctionMetadataManager_GetsMetadata_FromFunctionProviders()
        {
            var functionMetadataCollection = new Collection<FunctionMetadata>();
            var mockFunctionErrors = new Dictionary<string, ImmutableArray<string>>();
            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadata(false)).Returns(new Collection<FunctionMetadata>().ToImmutableArray());
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            functionMetadataCollection.Add(GetTestFunctionMetadata("somefile.dll", name: "anotherFunction"));

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object }, new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory());
            testFunctionMetadataManager.LoadFunctionMetadata();

            Assert.Equal(0, testFunctionMetadataManager.Errors.Count);
            Assert.Equal(1, testFunctionMetadataManager.GetFunctionMetadata(true).Length);
            Assert.Equal("anotherFunction", testFunctionMetadataManager.GetFunctionMetadata(true).FirstOrDefault()?.Name);
        }

        [Fact]
        public void FunctionMetadataManager_ThrowsError_DuplicateFunctions_FromFunctionProviders()
        {
            var functionMetadataCollection = new Collection<FunctionMetadata>();
            var mockFunctionErrors = new Dictionary<string, ImmutableArray<string>>();
            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();
            var mockFunctionProviderDuplicate = new Mock<IFunctionProvider>();

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadata(false)).Returns(new Collection<FunctionMetadata>().ToImmutableArray());
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            functionMetadataCollection.Add(GetTestFunctionMetadata("somefile.dll", name: "duplicateFunction"));

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            mockFunctionProviderDuplicate.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProviderDuplicate.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object, mockFunctionProviderDuplicate.Object },
                new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory());

            var ex = Assert.Throws<InvalidOperationException>(() => testFunctionMetadataManager.LoadFunctionMetadata());
            Assert.Equal("Found duplicate FunctionMetadata with the name duplicateFunction", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void IsScriptFileDetermined_ScriptFile_Emtpy_HttpWorker_Returns_True(string scriptFile)
        {
            FunctionMetadata functionMetadata = GetTestFunctionMetadata(scriptFile);
            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                _mockFunctionMetadataProvider.Object, new List<IFunctionProvider>(), new OptionsWrapper<HttpWorkerOptions>(GetTestHttpWorkerOptions()), MockNullLoggerFactory.CreateLoggerFactory());

            Assert.True(testFunctionMetadataManager.IsScriptFileDetermined(functionMetadata));
        }

        [Theory]
        [InlineData("run.csx")]
        [InlineData("run.py")]
        [InlineData("index.js")]
        [InlineData("test.dll")]
        public void ScriptFile_Emtpy_Returns_True(string scriptFile)
        {
            FunctionMetadata functionMetadata = GetTestFunctionMetadata(scriptFile);
            Assert.True(_testFunctionMetadataManager.IsScriptFileDetermined(functionMetadata));
        }

        private static HttpWorkerOptions GetTestHttpWorkerOptions()
        {
            return new HttpWorkerOptions()
            {
                Description = new HttpWorkerDescription()
                {
                    DefaultExecutablePath = "text.exe"
                }
            };
        }

        private static LanguageWorkerOptions GetTestLanguageWorkerOptions()
        {
            return new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
        }

        private static FunctionMetadata GetTestFunctionMetadata(string scriptFile, string name = "testFunction")
        {
            return new FunctionMetadata()
            {
                Name = name,
                ScriptFile = scriptFile,
                Language = "node"
            };
        }
    }
}
