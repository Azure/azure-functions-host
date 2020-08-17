// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http.Results;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class KeysControllerTests : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private Collection<FunctionDescriptor> _testFunctions;
        private KeysController _testController;
        private Mock<ISecretManager> _secretsManagerMock;
        private Mock<IFunctionsSyncManager> _functionsSyncManagerMock;

        public KeysControllerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _testFunctions = new Collection<FunctionDescriptor>();

            string rootScriptPath = @"c:\test\functions";
            var environment = new NullScriptHostEnvironment();
            var eventManager = new Mock<IScriptEventManager>();

            var settings = new WebHostSettings()
            {
                ScriptPath = rootScriptPath,
                SecretsPath = _secretsDirectory.Path
            };
            _secretsManagerMock = new Mock<ISecretManager>(MockBehavior.Strict);

            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var dirBase = new Mock<DirectoryBase>();

            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootScriptPath, "TestFunction1", ScriptConstants.FunctionMetadataFileName))).Returns("{}");
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootScriptPath, "TestFunction2", ScriptConstants.FunctionMetadataFileName))).Returns("{}");
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootScriptPath, "DNE", ScriptConstants.FunctionMetadataFileName))).Throws(new DirectoryNotFoundException());

            _functionsSyncManagerMock = new Mock<IFunctionsSyncManager>(MockBehavior.Strict);
            _functionsSyncManagerMock.Setup(p => p.TrySyncTriggersAsync(false)).ReturnsAsync(new SyncTriggersResult { Success = true });

            _testController = new KeysController(settings, _secretsManagerMock.Object, traceWriter, null, fileSystem.Object, _functionsSyncManagerMock.Object);

            var keys = new Dictionary<string, string>
            {
                { "key1", "secret1" }
            };
            _secretsManagerMock.Setup(p => p.GetFunctionSecretsAsync("TestFunction1", false)).ReturnsAsync(keys);

            keys = new Dictionary<string, string>
            {
                { "key1", "secret1" }
            };
            _secretsManagerMock.Setup(p => p.GetFunctionSecretsAsync("TestFunction2", false)).ReturnsAsync(keys);

            _secretsManagerMock.Setup(p => p.GetFunctionSecretsAsync("DNE", false)).ReturnsAsync((IDictionary<string, string>)null);
        }

        [Fact]
        public async Task GetKeys_ReturnsKeys()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/functions/keys");
            var result = (OkNegotiatedContentResult<ApiModel>)(await _testController.Get("TestFunction1"));

            var content = (JObject)result.Content;
            var keys = content["keys"];
            Assert.Equal("key1", keys[0]["name"]);
            Assert.Equal("secret1", keys[0]["value"]);
        }

        [Fact]
        public async Task GetKeys_NotAFunction_ReturnsNotFound()
        {
            var result = await _testController.Get("DNE");
            Assert.True(result is NotFoundResult);
        }

        [Fact]
        public async Task GetKeys_NotAKey_ReturnsNotFound()
        {
            var result = await _testController.Get("TestFunction1", "dne");
            Assert.True(result is NotFoundResult);
        }

        [Fact]
        public async Task PutKey_NotAFunction_ReturnsNotFound()
        {
            var key = new Key("key2", "secret2");

            var result = await _testController.Put("DNE", key.Name, key);
            Assert.True(result is NotFoundResult);

            _functionsSyncManagerMock.Verify(p => p.TrySyncTriggersAsync(false), Times.Never);
        }

        [Fact]
        public async Task PutKey_Succeeds()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/functions/keys/key2");

            var key = new Key("key2", "secret2");
            var keyOperationResult = new KeyOperationResult(key.Value, OperationResult.Updated);
            _secretsManagerMock.Setup(p => p.AddOrUpdateFunctionSecretAsync(key.Name, key.Value, "TestFunction1", ScriptSecretsType.Function)).ReturnsAsync(keyOperationResult);

            var result = (OkNegotiatedContentResult<ApiModel>)(await _testController.Put("TestFunction1", key.Name, key));
            var content = (JObject)result.Content;
            Assert.Equal("key2", content["name"]);
            Assert.Equal("secret2", content["value"]);

            _functionsSyncManagerMock.Verify(p => p.TrySyncTriggersAsync(false), Times.Once);
        }

        [Theory]
        [InlineData("key1", false)]
        [InlineData("_key1", false)]
        [InlineData("_master", true)]
        [InlineData("_MASter", true)]
        [InlineData(null, true)]
        public async Task DeleteKey_Tests(string keyName, bool invalidKey)
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/functions/keys/key2");

            _secretsManagerMock.Setup(p => p.DeleteSecretAsync(keyName, "TestFunction1", ScriptSecretsType.Function)).ReturnsAsync(true);

            if (invalidKey)
            {
                if (string.IsNullOrEmpty(keyName))
                {
                    var result = (BadRequestErrorMessageResult)(await _testController.Delete("TestFunction1", keyName));
                    Assert.Equal("Invalid key name.", result.Message);
                }
                else
                {
                    var result = (BadRequestErrorMessageResult)(await _testController.Delete("TestFunction1", keyName));
                    Assert.Equal("Cannot delete System Key.", result.Message);
                }
                _functionsSyncManagerMock.Verify(p => p.TrySyncTriggersAsync(false), Times.Never);
            }
            else
            {
                var result = (StatusCodeResult)(await _testController.Delete("TestFunction1", keyName));
                Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
                _functionsSyncManagerMock.Verify(p => p.TrySyncTriggersAsync(false), Times.Once);
            }
        }

        [Fact]
        public async Task DeleteKey_NotAFunction_ReturnsNotFound()
        {
            var result = await _testController.Delete("DNE", "key2");
            Assert.True(result is NotFoundResult);

            _functionsSyncManagerMock.Verify(p => p.TrySyncTriggersAsync(false), Times.Never);
        }

        [Fact]
        public async Task DeleteKey_NotAKey_ReturnsNotFound()
        {
            _secretsManagerMock.Setup(p => p.DeleteSecretAsync("dne", "TestFunction1", ScriptSecretsType.Function)).ReturnsAsync(false);

            var result = await _testController.Delete("TestFunction1", "dne");
            Assert.True(result is NotFoundResult);

            _functionsSyncManagerMock.Verify(p => p.TrySyncTriggersAsync(false), Times.Never);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _secretsDirectory.Dispose();
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
    }
}
