// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http.Results;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
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

        public KeysControllerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _testFunctions = new Collection<FunctionDescriptor>();

            var config = new ScriptHostConfiguration();
            config.TraceWriter = new TestTraceWriter(TraceLevel.Info);
            var environment = new NullScriptHostEnvironment();
            var eventManager = new Mock<IScriptEventManager>();

            WebHostSettings settings = new WebHostSettings();
            settings.SecretsPath = _secretsDirectory.Path;
            _secretsManagerMock = new Mock<ISecretManager>(MockBehavior.Strict);

            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            _testController = new KeysController(_secretsManagerMock.Object, traceWriter, null);

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
        public async Task GetKeys_NoSecrets_ReturnsNotFound()
        {
            var result = await _testController.Get("DNE");

            Assert.True(result is NotFoundResult);
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
        }

        [Fact]
        public async Task DeleteKey_Succeeds()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/functions/keys/key2");

            _secretsManagerMock.Setup(p => p.DeleteSecretAsync("key2", "TestFunction1", ScriptSecretsType.Function)).ReturnsAsync(true);

            var result = (StatusCodeResult)(await _testController.Delete("TestFunction1", "key2"));
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
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
