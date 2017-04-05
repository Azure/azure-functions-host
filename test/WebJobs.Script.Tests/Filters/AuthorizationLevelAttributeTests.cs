// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
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
            HttpConfig = new HttpConfiguration();
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

        protected HttpConfiguration HttpConfig { get; }

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

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Admin, level);
        }

        [Theory]
        [InlineData(TestHostFunctionKeyValue1, TestFunctionKeyValue1)]
        [InlineData(TestHostFunctionKeyValue2, TestFunctionKeyValue2)]
        public async Task GetAuthorizationLevel_ValidKeyHeader_FunctionKey_ReturnsFunction(string hostFunctionKeyValue, string functionKeyValue)
        {
            // first verify the host level function key works
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, hostFunctionKeyValue);
            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object);
            Assert.Equal(AuthorizationLevel.Function, level);

            // test function specific key
            request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, functionKeyValue);
            level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);
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

            Assert.True(methodReturned, $"{nameof(AuthorizationLevelAttribute.GetAuthorizationLevelAsync)} resolving secrets did not return.");
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

            Assert.True(methodReturned, $"{nameof(AuthorizationLevelAttribute.GetAuthorizationLevelAsync)} with cached host secrets did not return.");
        }

        [Fact]
        public void GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_AndCachedSecrets_DoesNotDeadlock()
        {
            var secretManager = CreateSecretManager(() => Task.FromResult(new HostSecretsInfo()), () => Task.FromResult(new Dictionary<string, string> { { "default", TestFunctionKeyValue1 } }));

            var methodReturned = GetAuthorizationLevelAsync_CalledWithSingleThreadedContext_DoesNotDeadlock(secretManager);

            Assert.True(methodReturned, $"{nameof(AuthorizationLevelAttribute.GetAuthorizationLevelAsync)} with cached secrets did not return.");
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

                AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, secretManager, functionName: "TestFunction")
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

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Anonymous, level);
        }

        [Fact]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_MasterKey_ReturnsAdmin()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", TestMasterKeyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Admin, level);
        }

        [Fact]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_SystemKey_ReturnsSystem()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", TestSystemKeyValue1));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.System, level);
        }

        [Theory]
        [InlineData(TestHostFunctionKeyValue1, TestFunctionKeyValue1)]
        [InlineData(TestHostFunctionKeyValue2, TestFunctionKeyValue2)]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_FunctionKey_ReturnsFunction(string hostFunctionKeyValue, string functionKeyValue)
        {
            // first try host level function key
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", hostFunctionKeyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);

            uri = new Uri(string.Format("http://functions/api/foo?code={0}", functionKeyValue));
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);
        }

        [Fact]
        public async Task GetAuthorizationLevel_InvalidCodeQueryParam_ReturnsAnonymous()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", "invalid"));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, MockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Anonymous, level);
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

        protected static HttpActionContext CreateActionContext(MethodInfo action, HttpConfiguration config = null)
        {
            config = config ?? new HttpConfiguration();
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
