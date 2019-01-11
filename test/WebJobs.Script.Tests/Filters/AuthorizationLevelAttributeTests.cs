﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class AuthorizationLevelAttributeTests
    {
        protected const string TestHostFunctionKeyName1 = "hostfunckey1";
        protected const string TestHostFunctionKeyValue1 = "jkl012";
        protected const string TestHostFunctionKeyName2 = "hostfunckey2";
        protected const string TestHostFunctionKeyValue2 = "mno345";
        protected const string TestFunctionKeyName1 = "funckey1";
        protected const string TestFunctionKeyValue1 = "def456";
        protected const string TestFunctionKeyName2 = "funckey2";
        protected const string TestFunctionKeyValue2 = "ghi789";
        protected const string TestSystemKeyName1 = "syskey1";
        protected const string TestSystemKeyValue1 = "sysabc123";
        protected const string TestSystemKeyName2 = "syskey2";
        protected const string TestSystemKeyValue2 = "sysdef123";
        protected const string TestMasterKeyValue = "abc123";

        private HttpActionContext _actionContext;
        private HostSecretsInfo _hostSecrets;
        private Dictionary<string, string> _functionSecrets;
        private WebHostSettings _webHostSettings;

        public AuthorizationLevelAttributeTests()
        {
            HttpConfig = new System.Web.Http.HttpConfiguration();
            _actionContext = CreateActionContext(typeof(TestController).GetMethod("Get"), HttpConfig);

            Mock<IDependencyResolver> mockDependencyResolver = new Mock<IDependencyResolver>(MockBehavior.Strict);
            HttpConfig.DependencyResolver = mockDependencyResolver.Object;
            MockSecretManager = new Mock<ISecretManager>(MockBehavior.Strict);
            _hostSecrets = new HostSecretsInfo
            {
                MasterKey = TestMasterKeyValue,
                FunctionKeys = new Dictionary<string, string>
                {
                    { TestHostFunctionKeyName1, TestHostFunctionKeyValue1 },
                    { TestHostFunctionKeyName2, TestHostFunctionKeyValue2 }
                },
                SystemKeys = new Dictionary<string, string>
                {
                    { TestSystemKeyName1, TestSystemKeyValue1 },
                    { TestSystemKeyName2, TestSystemKeyValue2 }
                }
            };
            MockSecretManager.Setup(p => p.GetHostSecretsAsync()).ReturnsAsync(_hostSecrets);
            _functionSecrets = new Dictionary<string, string>
            {
                { TestFunctionKeyName1,  TestFunctionKeyValue1 },
                { TestFunctionKeyName2,  TestFunctionKeyValue2 }
            };
            MockSecretManager.Setup(p => p.GetFunctionSecretsAsync(It.IsAny<string>(), false)).ReturnsAsync(_functionSecrets);
            mockDependencyResolver.Setup(p => p.GetService(typeof(ISecretManager))).Returns(MockSecretManager.Object);
            _webHostSettings = new WebHostSettings();
            mockDependencyResolver.Setup(p => p.GetService(typeof(WebHostSettings))).Returns(_webHostSettings);
        }

        protected System.Web.Http.HttpConfiguration HttpConfig { get; }

        protected Mock<ISecretManager> MockSecretManager { get; }

        [Fact]
        public async Task OnAuthorization_AdminLevel_ValidHeader_Succeeds()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, "abc123");
            _actionContext.ControllerContext.Request = request;

            await attribute.OnAuthorizationAsync(_actionContext, CancellationToken.None);

            Assert.Null(_actionContext.Response);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("invalid")]
        public async Task OnAuthorization_AdminLevel_InvalidHeader_ReturnsUnauthorized(string headerValue)
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            if (headerValue != null)
            {
                request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, headerValue);
            }
            _actionContext.ControllerContext.Request = request;

            await attribute.OnAuthorizationAsync(_actionContext, CancellationToken.None);

            HttpResponseMessage response = _actionContext.Response;
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task OnAuthorization_AdminLevel_NoMasterKeySet_ReturnsUnauthorized()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);
            _hostSecrets.MasterKey = null;

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestMasterKeyValue);
            _actionContext.ControllerContext.Request = request;

            await attribute.OnAuthorizationAsync(_actionContext, CancellationToken.None);

            HttpResponseMessage response = _actionContext.Response;
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task OnAuthorization_AnonymousLevel_Succeeds()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Anonymous);

            _actionContext.ControllerContext.Request = new HttpRequestMessage();

            await attribute.OnAuthorizationAsync(_actionContext, CancellationToken.None);

            Assert.Null(_actionContext.Response);
        }

        [Fact]
        public async Task GetAuthorizationLevel_ValidKeyHeader_MasterKey_ReturnsAdmin()
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestMasterKeyValue);

            var result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Admin, result.AuthorizationLevel);
        }

        [Theory]
        [InlineData(TestHostFunctionKeyValue1, TestFunctionKeyValue1)]
        [InlineData(TestHostFunctionKeyValue2, TestFunctionKeyValue2)]
        public async Task GetAuthorizationLevel_ValidKeyHeader_FunctionKey_ReturnsFunction(string hostFunctionKeyValue, string functionKeyValue)
        {
            // first verify the host level function key works
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, hostFunctionKeyValue);
            var result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object);
            Assert.Equal(AuthorizationLevel.Function, result.AuthorizationLevel);

            // test function specific key
            request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, functionKeyValue);
            result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, result.AuthorizationLevel);
        }

        [Fact]
        public void GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_ResolvingSecrets_DoesNotDeadlock()
        {
            // Test resolving secrets
            var secretManager = CreateSecretManager(
                async () =>
                {
                    await Task.Delay(500);
                    return new HostSecretsInfo();
                },
                async () =>
                {
                    await Task.Delay(500);
                    return new Dictionary<string, string> { { "default", TestFunctionKeyValue1 } };
                });

            bool methodReturned = GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_DoesNotDeadlock(secretManager);

            Assert.True(methodReturned, $"{nameof(AuthorizationLevelAttribute.GetAuthorizationResultAsync)} resolving secrets did not return.");
        }

        [Fact]
        public void GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_AndCachedHostSecrets_DoesNotDeadlock()
        {
            var secretManager = CreateSecretManager(() => Task.FromResult(new HostSecretsInfo()), async () =>
            {
                await Task.Delay(500);
                return new Dictionary<string, string> { { "default", TestFunctionKeyValue1 } };
            });

            var methodReturned = GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_DoesNotDeadlock(secretManager);

            Assert.True(methodReturned, $"{nameof(AuthorizationLevelAttribute.GetAuthorizationResultAsync)} with cached host secrets did not return.");
        }

        [Fact]
        public void GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_AndCachedSecrets_DoesNotDeadlock()
        {
            var secretManager = CreateSecretManager(() => Task.FromResult(new HostSecretsInfo()), () => Task.FromResult(new Dictionary<string, string> { { "default", TestFunctionKeyValue1 } }));

            var methodReturned = GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_DoesNotDeadlock(secretManager);

            Assert.True(methodReturned, $"{nameof(AuthorizationLevelAttribute.GetAuthorizationResultAsync)} with cached secrets did not return.");
        }

        private ISecretManager CreateSecretManager(Func<Task<HostSecretsInfo>> hostSecrets, Func<Task<Dictionary<string, string>>> functionSecrets)
        {
            var secretManagerMock = new Mock<ISecretManager>();

            secretManagerMock.Setup(m => m.GetHostSecretsAsync())
            .Returns(() => Task.FromResult(new HostSecretsInfo()));

            secretManagerMock.Setup(m => m.GetFunctionSecretsAsync(It.IsAny<string>(), false))
            .Returns(async () =>
            {
                await Task.Delay(500);
                return new Dictionary<string, string> { { "default", TestFunctionKeyValue1 } };
            });

            return secretManagerMock.Object;
        }

        private bool GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_DoesNotDeadlock(ISecretManager secretManager)
        {
            var resetEvent = new ManualResetEvent(false);

            var thread = new Thread(() =>
            {
                HttpRequestMessage request = new HttpRequestMessage();
                request = new HttpRequestMessage();
                request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestFunctionKeyValue1);

                var context = new SingleThreadedSynchronizationContext(true);
                SynchronizationContext.SetSynchronizationContext(context);

                AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, secretManager, functionName: "TestFunction")
                .ContinueWith(t =>
                {
                    context.Stop();

                    resetEvent.Set();
                });

                context.Run();
            });

            thread.IsBackground = true;
            thread.Start();

            bool eventSignaled = resetEvent.WaitOne(TimeSpan.FromSeconds(5));

            thread.Abort();

            return eventSignaled;
        }

        [Fact]
        public async Task GetAuthorizationLevel_InvalidKeyHeader_ReturnsAnonymous()
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, "invalid");

            var result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Anonymous, result.AuthorizationLevel);
        }

        [Fact]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_MasterKey_ReturnsAdmin()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", TestMasterKeyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            var result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Admin, result.AuthorizationLevel);
        }

        [Fact]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_SystemKey_ReturnsSystem()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", TestSystemKeyValue1));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            var result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.System, result.AuthorizationLevel);
        }

        [Theory]
        [InlineData(TestHostFunctionKeyValue1, TestFunctionKeyValue1)]
        [InlineData(TestHostFunctionKeyValue2, TestFunctionKeyValue2)]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_FunctionKey_ReturnsFunction(string hostFunctionKeyValue, string functionKeyValue)
        {
            // first try host level function key
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", hostFunctionKeyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            var result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, result.AuthorizationLevel);

            uri = new Uri(string.Format("http://functions/api/foo?code={0}", functionKeyValue));
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, result.AuthorizationLevel);
        }

        [Fact]
        public async Task GetAuthorizationLevel_InvalidCodeQueryParam_ReturnsAnonymous()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", "invalid"));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            var result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Anonymous, result.AuthorizationLevel);
        }

        [Fact]
        public static void SkipAuthorization_AllowAnonymous_MethodLevel_ReturnsTrue()
        {
            var actionContext = CreateActionContext(typeof(TestController).GetMethod("GetAnonymous"));
            Assert.True(AuthorizationLevelAttribute.SkipAuthorization(actionContext));
        }

        [Fact]
        public static void SkipAuthorization_AllowAnonymous_ClassLevel_ReturnsTrue()
        {
            var actionContext = CreateActionContext(typeof(AnonymousTestController).GetMethod("Get"));
            Assert.True(AuthorizationLevelAttribute.SkipAuthorization(actionContext));

            actionContext = CreateActionContext(typeof(AnonymousTestController).GetMethod("GetAnonymous"));
            Assert.True(AuthorizationLevelAttribute.SkipAuthorization(actionContext));
        }

        [Fact]
        public static void SkipAuthorization_AllowAnonymousNotSpecified_ReturnsFalse()
        {
            var actionContext = CreateActionContext(typeof(TestController).GetMethod("Get"));
            Assert.False(AuthorizationLevelAttribute.SkipAuthorization(actionContext));
        }

        [Theory]
        [InlineData(TestHostFunctionKeyName1, TestHostFunctionKeyValue1, AuthorizationLevel.Function, null)]
        [InlineData(TestHostFunctionKeyName2, TestHostFunctionKeyValue2, AuthorizationLevel.Function, null)]
        [InlineData(TestFunctionKeyName1, TestFunctionKeyValue1, AuthorizationLevel.Function, "TestFunction")]
        [InlineData(null, TestFunctionKeyValue1, AuthorizationLevel.Function, "TestFunction")]
        [InlineData(TestFunctionKeyName2, TestFunctionKeyValue2, AuthorizationLevel.Function, "TestFunction")]
        [InlineData(null, TestFunctionKeyValue2, AuthorizationLevel.Function, "TestFunction")]
        [InlineData("", TestMasterKeyValue, AuthorizationLevel.Admin, null)]
        [InlineData(TestSystemKeyName1, TestSystemKeyValue1, AuthorizationLevel.System, null)]
        [InlineData(TestSystemKeyName2, TestSystemKeyValue2, AuthorizationLevel.System, null)]
        [InlineData("foo", TestSystemKeyValue1, AuthorizationLevel.Anonymous, null)]
        [InlineData(TestSystemKeyName1, "bar", AuthorizationLevel.Anonymous, null)]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_WithNamedKeyRequirement_ReturnsExpectedLevel(string keyName, string keyValue, AuthorizationLevel expectedLevel, string functionName = null)
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", keyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            Func<IDictionary<string, string>, string, string, string> evaluator = (secrets, name, value) => AuthorizationLevelAttribute.GetKeyMatchOrNull(secrets, name, value);

            var result = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, MockSecretManager.Object, evaluator, keyName: keyName, functionName: functionName);

            Assert.Equal(expectedLevel, result.AuthorizationLevel);
        }

        [Fact]
        public async Task OnAuthorization_WithNamedKeyHeader_Succeeds()
        {
            var attribute = new AuthorizationLevelAttribute(AuthorizationLevel.System, TestSystemKeyName1);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestSystemKeyValue1);
            var actionContext = CreateActionContext(typeof(TestController).GetMethod(nameof(TestController.Get)), HttpConfig);
            actionContext.ControllerContext.Request = request;

            await attribute.OnAuthorizationAsync(actionContext, CancellationToken.None);

            Assert.Null(actionContext.Response);
        }

        [Fact]
        public async Task OnAuthorization_Arm_Success_SetsAdminAuthLevel()
        {
            byte[] key = GenerateKeyBytes();
            string keyString = GenerateKeyHexString(key);
            string token = CreateSimpleWebToken(DateTime.UtcNow.AddMinutes(5), key);

            var attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.ArmTokenHeaderName, token);
            var actionContext = CreateActionContext(typeof(TestController).GetMethod(nameof(TestController.Get)), HttpConfig);
            actionContext.ControllerContext.Request = request;

            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebsiteAuthEncryptionKey, keyString))
            {
                await attribute.OnAuthorizationAsync(actionContext, CancellationToken.None);
            }

            Assert.Null(actionContext.Response);
            Assert.Equal(AuthorizationLevel.Admin, actionContext.Request.GetAuthorizationLevel());
        }

        [Fact]
        public async Task OnAuthorization_Arm_Expired_ReturnsUnauthorized()
        {
            byte[] key = GenerateKeyBytes();
            string keyString = GenerateKeyHexString(key);
            string token = CreateSimpleWebToken(DateTime.UtcNow.AddMinutes(-5), key);

            var attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.ArmTokenHeaderName, token);
            var actionContext = CreateActionContext(typeof(TestController).GetMethod(nameof(TestController.Get)), HttpConfig);
            actionContext.ControllerContext.Request = request;

            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebsiteAuthEncryptionKey, keyString))
            {
                await attribute.OnAuthorizationAsync(actionContext, CancellationToken.None);
            }

            Assert.Equal(HttpStatusCode.Unauthorized, actionContext.Response.StatusCode);
            Assert.Equal(AuthorizationLevel.Anonymous, actionContext.Request.GetAuthorizationLevel());
        }

        [Fact]
        public async Task OnAuthorization_Arm_Invalid_ReturnsUnauthorized()
        {
            byte[] key = GenerateKeyBytes();
            string keyString = GenerateKeyHexString(key);
            string token = CreateSimpleWebToken(DateTime.UtcNow.AddMinutes(5), key);
            token = token.Substring(0, token.Length - 5);

            var attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.ArmTokenHeaderName, token);
            var actionContext = CreateActionContext(typeof(TestController).GetMethod(nameof(TestController.Get)), HttpConfig);
            actionContext.ControllerContext.Request = request;

            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebsiteAuthEncryptionKey, keyString))
            {
                await attribute.OnAuthorizationAsync(actionContext, CancellationToken.None);
            }

            Assert.Equal(HttpStatusCode.Unauthorized, actionContext.Response.StatusCode);
            Assert.Equal(AuthorizationLevel.Anonymous, actionContext.Request.GetAuthorizationLevel());
        }

        protected static HttpActionContext CreateActionContext(MethodInfo action, System.Web.Http.HttpConfiguration config = null)
        {
            config = config ?? new System.Web.Http.HttpConfiguration();
            var actionContext = new HttpActionContext();
            var controllerDescriptor = new HttpControllerDescriptor(config, action.ReflectedType.Name, action.ReflectedType);
            var controllerContext = new HttpControllerContext();
            controllerContext.ControllerDescriptor = controllerDescriptor;
            actionContext.ControllerContext = controllerContext;
            var actionDescriptor = new ReflectedHttpActionDescriptor(controllerDescriptor, action);
            actionContext.ActionDescriptor = actionDescriptor;
            controllerContext.Configuration = config;

            return actionContext;
        }

        public static byte[] GenerateKeyBytes()
        {
            using (var aes = new AesManaged())
            {
                aes.GenerateKey();
                return aes.Key;
            }
        }

        public static string GenerateKeyHexString(byte[] key = null)
        {
            return BitConverter.ToString(key ?? GenerateKeyBytes()).Replace("-", string.Empty);
        }

        private static string CreateSimpleWebToken(DateTime validUntil, byte[] key = null) => EncryptSimpleWebToken($"exp={validUntil.Ticks}", key);

        private static string EncryptSimpleWebToken(string value, byte[] key = null)
        {
            if (key == null)
            {
                key = SimpleWebTokenHelper.GetEncryptionKey(EnvironmentSettingNames.WebsiteAuthEncryptionKey);
            }

            using (var aes = new AesManaged { Key = key })
            {
                // IV is always generated for the key every time
                aes.GenerateIV();
                var input = Encoding.UTF8.GetBytes(value);
                var iv = Convert.ToBase64String(aes.IV);

                using (var encrypter = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var cipherStream = new MemoryStream())
                {
                    using (var cryptoStream = new CryptoStream(cipherStream, encrypter, CryptoStreamMode.Write))
                    using (var binaryWriter = new BinaryWriter(cryptoStream))
                    {
                        binaryWriter.Write(input);
                        cryptoStream.FlushFinalBlock();
                    }

                    // return {iv}.{swt}.{sha236(key)}
                    return string.Format("{0}.{1}.{2}", iv, Convert.ToBase64String(cipherStream.ToArray()), GetSHA256Base64String(aes.Key));
                }
            }
        }

        private static string GetSHA256Base64String(byte[] key)
        {
            using (var sha256 = new SHA256Managed())
            {
                return Convert.ToBase64String(sha256.ComputeHash(key));
            }
        }

        public class TestController : ApiController
        {
            public void Get()
            {
            }

            [AllowAnonymous]
            public void GetAnonymous()
            {
            }
        }

        [AllowAnonymous]
        public class AnonymousTestController : ApiController
        {
            public void Get()
            {
            }

            [AllowAnonymous]
            public void GetAnonymous()
            {
            }
        }
    }
}
