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
        private Mock<ScriptHost> _hostMock;
        private Mock<WebScriptHostManager> _managerMock;
        private Collection<FunctionDescriptor> _testFunctions;
        private Dictionary<string, Collection<string>> _testFunctionErrors;
        private KeysController _testController;
        private Mock<ISecretManager> _secretsManagerMock;

        public KeysControllerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _testFunctions = new Collection<FunctionDescriptor>();
            _testFunctionErrors = new Dictionary<string, Collection<string>>();

            var config = new ScriptHostConfiguration();
            var environment = new NullScriptHostEnvironment();
            var eventManager = new Mock<IScriptEventManager>();
            _hostMock = new Mock<ScriptHost>(MockBehavior.Strict, new object[] { environment, eventManager.Object, config, null });
            _hostMock.Setup(p => p.Functions).Returns(_testFunctions);
            _hostMock.Setup(p => p.FunctionErrors).Returns(_testFunctionErrors);

            WebHostSettings settings = new WebHostSettings();
            settings.SecretsPath = _secretsDirectory.Path;
            _secretsManagerMock = new Mock<ISecretManager>(MockBehavior.Strict);
            _managerMock = new Mock<WebScriptHostManager>(MockBehavior.Strict, new object[] { config, new TestSecretManagerFactory(_secretsManagerMock.Object), eventManager.Object, _settingsManager, settings });
            _managerMock.SetupGet(p => p.Instance).Returns(_hostMock.Object);

            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            _testController = new KeysController(_managerMock.Object, _secretsManagerMock.Object, traceWriter, null);

            // setup some test functions
            string errorFunction = "ErrorFunction";
            var errors = new Collection<string>();
            errors.Add("A really really bad error!");
            _testFunctionErrors.Add(errorFunction, errors);

            var keys = new Dictionary<string, string>
            {
                { "key1", "secret1" }
            };
            _secretsManagerMock.Setup(p => p.GetFunctionSecretsAsync(errorFunction, false)).ReturnsAsync(keys);
        }

        [Fact]
        public async Task GetKeys_FunctionInError_ReturnsKeys()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/functions/keys");
            var result = (OkNegotiatedContentResult<ApiModel>)(await _testController.Get("ErrorFunction"));

            var content = (JObject)result.Content;
            var keys = content["keys"];
            Assert.Equal("key1", keys[0]["name"]);
            Assert.Equal("secret1", keys[0]["value"]);
        }

        [Fact]
        public async Task PutKey_FunctionInError_Succeeds()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/functions/keys/key2");

            var key = new Key("key2", "secret2");
            var keyOperationResult = new KeyOperationResult(key.Value, OperationResult.Updated);
            _secretsManagerMock.Setup(p => p.AddOrUpdateFunctionSecretAsync(key.Name, key.Value, "ErrorFunction", ScriptSecretsType.Function)).ReturnsAsync(keyOperationResult);

            var result = (OkNegotiatedContentResult<ApiModel>)(await _testController.Put("ErrorFunction", key.Name, key));
            var content = (JObject)result.Content;
            Assert.Equal("key2", content["name"]);
            Assert.Equal("secret2", content["value"]);
        }

        [Fact]
        public async Task DeleteKey_FunctionInError_Succeeds()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/functions/keys/key2");

            _secretsManagerMock.Setup(p => p.DeleteSecretAsync("key2", "ErrorFunction", ScriptSecretsType.Function)).ReturnsAsync(true);

            var result = (StatusCodeResult)(await _testController.Delete("ErrorFunction", "key2"));
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
