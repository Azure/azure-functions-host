// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;
using static Microsoft.Azure.AppService.Proxy.Common.Constants.WellKnownHttpHeaders;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionMetadataManagerTests
    {
        private const string _expectedErrorMessage = "Unable to determine the primary function script.Make sure atleast one script file is present.Try renaming your entry point script to 'run' or alternatively you can specify the name of the entry point script explicitly by adding a 'scriptFile' property to your function metadata.";
        private ScriptJobHostOptions _scriptJobHostOptions = new ScriptJobHostOptions();
        private Mock<IFunctionMetadataProvider> _mockFunctionMetadataProvider;
        private FunctionMetadataManager _testFunctionMetadataManager;
        private HttpWorkerOptions _defaultHttpWorkerOptions;
        private Mock<IScriptHostManager> _mockScriptHostManager;

        public FunctionMetadataManagerTests()
        {
            _mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "node");
            _defaultHttpWorkerOptions = new HttpWorkerOptions();
            _scriptJobHostOptions.RootScriptPath = functionsPath;

            _mockScriptHostManager = new Mock<IScriptHostManager>();
            _testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(
                new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                _mockScriptHostManager,
                _mockFunctionMetadataProvider.Object,
                new List<IFunctionProvider>(),
                new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions),
                MockNullLoggerFactory.CreateLoggerFactory(),
                new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void IsScriptFileDetermined_ScriptFile_Emtpy_False(string scriptFile)
        {
            _mockScriptHostManager.Raise(m => m.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, new Mock<IHost>().Object));
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
            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), SystemEnvironment.Instance, false)).Returns(Task.FromResult(functionMetadataCollection.ToImmutableArray()));
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors);

            var managerMock = new Mock<IScriptHostManager>();
            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions), managerMock,
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>(), new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory(), new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));

            managerMock.Raise(m => m.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, new Mock<IHost>().Object));
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
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false)).Returns(Task.FromResult(new Collection<FunctionMetadata>().ToImmutableArray()));
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            functionMetadataCollection.Add(GetTestFunctionMetadata(scriptFile));
            functionMetadataCollection.Add(GetTestFunctionMetadata(scriptFile, name: "anotherFunction"));
            mockFunctionErrors["anotherFunction"] = new List<string>() { "error" }.ToImmutableArray();

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            var managerMock = new Mock<IScriptHostManager>();
            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions), managerMock,
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object }, new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory(), new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));

            managerMock.Raise(m => m.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, new Mock<IHost>().Object));

            testFunctionMetadataManager.LoadFunctionMetadata();

            Assert.Equal(2, testFunctionMetadataManager.Errors.Count);
            ImmutableArray<string> functionErrors = testFunctionMetadataManager.Errors["anotherFunction"];
            Assert.Equal(2, functionErrors.Length);
            Assert.True(functionErrors.Contains("error"));
        }

        [Fact]
        public void FunctionMetadataManager_DoesNotError_MissingScriptFile_InWebHostMode()
        {
            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();

            var testMetadata = GetTestFunctionMetadata(null);

            var managerMock = new Mock<IScriptHostManager>();
            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions), managerMock,
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object }, new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory(), new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));

            Assert.True(testFunctionMetadataManager.IsScriptFileDetermined(testMetadata));

            managerMock.Raise(m => m.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, new Mock<IHost>().Object));

            Assert.False(testFunctionMetadataManager.IsScriptFileDetermined(testMetadata));
        }

        [Fact]
        public void FunctionMetadataManager_GetsMetadata_FromFunctionProviders()
        {
            var functionMetadataCollection = new Collection<FunctionMetadata>();
            var mockFunctionErrors = new Dictionary<string, ImmutableArray<string>>();
            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();
            var testLoggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(testLoggerProvider);

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false)).Returns(Task.FromResult(new Collection<FunctionMetadata>().ToImmutableArray()));
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            functionMetadataCollection.Add(GetTestFunctionMetadata("somefile.dll", name: "anotherFunction"));

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object }, new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), loggerFactory, new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));
            testFunctionMetadataManager.LoadFunctionMetadata();

            Assert.Equal(0, testFunctionMetadataManager.Errors.Count);
            Assert.Equal(1, testFunctionMetadataManager.GetFunctionMetadata(true).Length);
            var traces = testLoggerProvider.GetAllLogMessages();
            Assert.Equal("anotherFunction", testFunctionMetadataManager.GetFunctionMetadata(true).FirstOrDefault()?.Name);

            // Assert logging traces print out as expected
            Assert.Equal(7, traces.Count);
            Assert.Equal(2, traces.Count(t => t.FormattedMessage.Contains("Reading functions metadata (Custom)")));
            Assert.Equal(2, traces.Count(t => t.FormattedMessage.Contains("1 functions found (Custom)")));
            Assert.Equal(1, traces.Count(t => t.FormattedMessage.Contains("1 functions loaded")));
            Assert.Equal(2, traces.Count(t => t.FormattedMessage.Contains("Loading functions metadata")));
        }

        [Fact]
        public void FunctionMetadataManager_IgnoresMetadata_FromFunctionProviders()
        {
            var functionMetadataCollection = new Collection<FunctionMetadata>();
            var mockFunctionErrors = new Dictionary<string, ImmutableArray<string>>();

            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false)).Returns(Task.FromResult(new Collection<FunctionMetadata>().ToImmutableArray()));
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            functionMetadataCollection.Add(GetTestFunctionMetadata("somefile.dll", name: "anotherFunction"));

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object }, new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory(), new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));

            Assert.Equal(0, testFunctionMetadataManager.GetFunctionMetadata(true, includeCustomProviders: false).Length);
            Assert.Equal(0, testFunctionMetadataManager.Errors.Count);
        }

        [Fact]
        public void FunctionMetadataManager_SortsMetadata_FromFunctionProviders()
        {
            var functionMetadataCollection = new Collection<FunctionMetadata>();
            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();

            var workerConfigs = TestHelpers.GetTestWorkerConfigs();

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false)).Returns(Task.FromResult(new Collection<FunctionMetadata>().ToImmutableArray()));
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            const string aFunction = "aFunction";
            const string bFunction = "bFunction";
            const string cFunction = "cFunction";

            // Add in unsorted order
            functionMetadataCollection.Add(GetTestFunctionMetadata("b.dll", name: bFunction));
            functionMetadataCollection.Add(GetTestFunctionMetadata("a.dll", name: aFunction));
            functionMetadataCollection.Add(GetTestFunctionMetadata("c.dll", name: cFunction));
            functionMetadataCollection.Add(GetTestFunctionMetadata("null.dll", name: null));

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object }, new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory(), new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));
            var functionMetadata = testFunctionMetadataManager.LoadFunctionMetadata();

            Assert.Equal(4, functionMetadata.Length);

            Assert.Null(functionMetadata[0].Name);
            Assert.True(string.Equals(aFunction, functionMetadata[1].Name));
            Assert.True(string.Equals(bFunction, functionMetadata[2].Name));
            Assert.True(string.Equals(cFunction, functionMetadata[3].Name));
        }

        [Fact]
        public void FunctionMetadataManager_ThrowsError_DuplicateFunctions_FromFunctionProviders()
        {
            var functionMetadataCollection = new Collection<FunctionMetadata>();
            var mockFunctionErrors = new Dictionary<string, ImmutableArray<string>>();
            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();
            var mockFunctionProviderDuplicate = new Mock<IFunctionProvider>();
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false)).Returns(Task.FromResult(new Collection<FunctionMetadata>().ToImmutableArray()));
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            functionMetadataCollection.Add(GetTestFunctionMetadata("somefile.dll", name: "duplicateFunction"));

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            mockFunctionProviderDuplicate.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProviderDuplicate.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions),
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object, mockFunctionProviderDuplicate.Object },
                new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory(),
                new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));

            var ex = Assert.Throws<InvalidOperationException>(() => testFunctionMetadataManager.LoadFunctionMetadata());
            Assert.Equal("Found duplicate FunctionMetadata with the name duplicateFunction", ex.Message);
        }

        [Fact]
        public void FunctionMetadataManager_ResetProviders_OnRefresh()
        {
            var functionMetadataCollection = new Collection<FunctionMetadata>();
            var mockFunctionErrors = new Dictionary<string, ImmutableArray<string>>();
            var mockFunctionMetadataProvider = new Mock<IFunctionMetadataProvider>();
            var mockFunctionProvider = new Mock<IFunctionProvider>();
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();

            mockFunctionMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false)).Returns(Task.FromResult(new Collection<FunctionMetadata>().ToImmutableArray()));
            mockFunctionMetadataProvider.Setup(m => m.FunctionErrors).Returns(new Dictionary<string, ICollection<string>>().ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()));

            functionMetadataCollection.Add(GetTestFunctionMetadata("somefile.dll", name: "myFunction"));

            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());
            mockFunctionProvider.Setup(m => m.FunctionErrors).Returns(mockFunctionErrors.ToImmutableDictionary());

            var managerMock = new Mock<IScriptHostManager>();

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions), managerMock,
                mockFunctionMetadataProvider.Object, new List<IFunctionProvider>() { mockFunctionProvider.Object }, new OptionsWrapper<HttpWorkerOptions>(_defaultHttpWorkerOptions), MockNullLoggerFactory.CreateLoggerFactory(), new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));

            testFunctionMetadataManager.LoadFunctionMetadata();

            Assert.Equal(0, testFunctionMetadataManager.Errors.Count);
            Assert.Equal(1, testFunctionMetadataManager.GetFunctionMetadata(true).Length);
            Assert.Equal("myFunction", testFunctionMetadataManager.GetFunctionMetadata(true).FirstOrDefault()?.Name);

            functionMetadataCollection = new Collection<FunctionMetadata>
            {
                GetTestFunctionMetadata("somefile.dll", name: "newFunction")
            };

            mockFunctionProvider = new Mock<IFunctionProvider>();
            mockFunctionProvider.Setup(m => m.GetFunctionMetadataAsync()).ReturnsAsync(functionMetadataCollection.ToImmutableArray());

            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IEnumerable<IFunctionProvider>))).Returns(new List<IFunctionProvider>() { mockFunctionProvider.Object });
            testFunctionMetadataManager.LoadFunctionMetadata();

            Assert.Equal(0, testFunctionMetadataManager.Errors.Count);
            Assert.Equal(1, testFunctionMetadataManager.GetFunctionMetadata(true).Length);
            Assert.Equal("newFunction", testFunctionMetadataManager.GetFunctionMetadata(true).FirstOrDefault()?.Name);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void IsScriptFileDetermined_ScriptFile_Emtpy_HttpWorker_Returns_True(string scriptFile)
        {
            FunctionMetadata functionMetadata = GetTestFunctionMetadata(scriptFile);

            var managerMock = new Mock<IScriptHostManager>();

            FunctionMetadataManager testFunctionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions), managerMock,
                _mockFunctionMetadataProvider.Object, new List<IFunctionProvider>(), new OptionsWrapper<HttpWorkerOptions>(GetTestHttpWorkerOptions()), MockNullLoggerFactory.CreateLoggerFactory(), new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));
            managerMock.Raise(m => m.HostInitializing += null, new EventArgs());

            Assert.True(testFunctionMetadataManager.IsScriptFileDetermined(functionMetadata));
        }

        [Theory]
        [InlineData("run.csx")]
        [InlineData("run.py")]
        [InlineData("index.js")]
        [InlineData("index.cjs")]
        [InlineData("index.mjs")]
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
