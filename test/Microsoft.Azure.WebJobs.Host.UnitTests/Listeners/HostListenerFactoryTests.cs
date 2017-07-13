// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Listeners
{
    public class HostListenerFactoryTests
    {
        public HostListenerFactoryTests()
        {
            DisableProvider_Static.Method = null;
            DisableProvider_Instance.Method = null;
        }

        [Theory]
        [InlineData(typeof(Functions1), "DisabledAtParameterLevel")]
        [InlineData(typeof(Functions1), "DisabledAtMethodLevel")]
        [InlineData(typeof(Functions1), "DisabledAtMethodLevel_Boolean")]
        [InlineData(typeof(Functions1), "DisabledAtMethodLevel_CustomType_Static")]
        [InlineData(typeof(Functions1), "DisabledAtMethodLevel_CustomType_Instance")]
        [InlineData(typeof(Functions1), "DisabledByEnvironmentSetting")]
        [InlineData(typeof(Functions2), "DisabledAtClassLevel")]
        public async Task CreateAsync_SkipsDisabledFunctions(Type jobType, string methodName)
        {
            Environment.SetEnvironmentVariable("EnvironmentSettingTrue", "True");

            Mock<IFunctionDefinition> mockFunctionDefinition = new Mock<IFunctionDefinition>();
            Mock<IFunctionInstanceFactory> mockInstanceFactory = new Mock<IFunctionInstanceFactory>(MockBehavior.Strict);
            Mock<IListenerFactory> mockListenerFactory = new Mock<IListenerFactory>(MockBehavior.Strict);
            SingletonManager singletonManager = new SingletonManager();
            TestTraceWriter traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            ILoggerFactory loggerFactory = new LoggerFactory();
            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            // create a bunch of function definitions that are disabled
            List<FunctionDefinition> functions = new List<FunctionDefinition>();
            var method = jobType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            FunctionDescriptor descriptor = FunctionIndexer.FromMethod(method, DefaultJobActivator.Instance); 
            FunctionDefinition definition = new FunctionDefinition(descriptor, mockInstanceFactory.Object, mockListenerFactory.Object);
            functions.Add(definition);

            // Create the composite listener - this will fail if any of the
            // function definitions indicate that they are not disabled
            HostListenerFactory factory = new HostListenerFactory(functions, singletonManager, DefaultJobActivator.Instance, null, traceWriter, loggerFactory);
            IListener listener = await factory.CreateAsync(CancellationToken.None);

            string expectedMessage = $"Function '{descriptor.ShortName}' is disabled";

            // Validate TraceWriter
            Assert.Equal(1, traceWriter.Traces.Count);
            Assert.Equal(TraceLevel.Info, traceWriter.Traces[0].Level);
            Assert.Equal(TraceSource.Host, traceWriter.Traces[0].Source);
            Assert.Equal(expectedMessage, traceWriter.Traces[0].Message);

            // Validate Logger
            var logMessage = loggerProvider.CreatedLoggers.Single().LogMessages.Single();
            Assert.Equal(LogLevel.Information, logMessage.Level);
            Assert.Equal(Logging.LogCategories.Startup, logMessage.Category);
            Assert.Equal(expectedMessage, logMessage.FormattedMessage);

            Environment.SetEnvironmentVariable("EnvironmentSettingTrue", null);
        }

        [Fact]
        public void IsDisabledByProvider_ValidProvider_InvokesProvider()
        {
            Assert.Null(DisableProvider_Static.Method);
            MethodInfo method = typeof(Functions1).GetMethod("DisabledAtMethodLevel_CustomType_Static", BindingFlags.Public | BindingFlags.Static);
            HostListenerFactory.IsDisabledByProvider(typeof(DisableProvider_Static), method, DefaultJobActivator.Instance);
            Assert.Same(method, DisableProvider_Static.Method);

            Assert.Null(DisableProvider_Instance.Method);
            method = typeof(Functions1).GetMethod("DisabledAtMethodLevel_CustomType_Static", BindingFlags.Public | BindingFlags.Static);
            HostListenerFactory.IsDisabledByProvider(typeof(DisableProvider_Instance), method, DefaultJobActivator.Instance);
            Assert.Same(method, DisableProvider_Static.Method);
        }

        [Theory]
        [InlineData(typeof(InvalidProvider_MissingFunction))]
        [InlineData(typeof(InvalidProvider_InvalidSignature))]
        public void IsDisabledByProvider_InvalidProvider_Throws(Type providerType)
        {
            MethodInfo method = typeof(Functions1).GetMethod("DisabledAtMethodLevel_CustomType_Static", BindingFlags.Public | BindingFlags.Static);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            {
                HostListenerFactory.IsDisabledByProvider(providerType, method, DefaultJobActivator.Instance);
            });
            Assert.Equal(string.Format("Type '{0}' must declare a method 'IsDisabled' returning bool and taking a single parameter of Type MethodInfo.", providerType.Name), ex.Message);
        }

        [Theory]
        [InlineData("Disable_{MethodName}_%Test%", true)]
        [InlineData("Disable_{MethodShortName}_%Test%", true)]
        [InlineData("Disable_TestJob", false)]
        [InlineData("Disable_TestJob_Blah", false)]
        public void IsDisabledBySetting_BindsSettingName(string settingName, bool disabled)
        {
            Environment.SetEnvironmentVariable("Disable_Functions1.TestJob_TestValue", "1");
            Environment.SetEnvironmentVariable("Disable_TestJob_TestValue", "1");
            Environment.SetEnvironmentVariable("Disable_TestJob", "False");

            Mock<INameResolver> mockNameResolver = new Mock<INameResolver>(MockBehavior.Strict);
            mockNameResolver.Setup(p => p.Resolve("Test")).Returns("TestValue");

            MethodInfo method = typeof(Functions1).GetMethod("TestJob", BindingFlags.Public | BindingFlags.Static);

            bool result = HostListenerFactory.IsDisabledBySetting(settingName, method, mockNameResolver.Object);
            Assert.Equal(result, disabled);

            Environment.SetEnvironmentVariable("Disable_Functions1.TestJob_TestValue", null);
            Environment.SetEnvironmentVariable("Disable_TestJob_TestValue", null);
            Environment.SetEnvironmentVariable("Disable_TestJob", null);
        }

        public static class Functions1
        {
            public static void DisabledAtParameterLevel(
                [Disable("DisableSettingTrue")]
                [QueueTrigger("test")] string message)
            {
            }

            [Disable("DisableSetting1")]
            public static void DisabledAtMethodLevel(
                [QueueTrigger("test")] string message)
            {
            }

            [Disable]
            public static void DisabledAtMethodLevel_Boolean(
                [QueueTrigger("test")] string message)
            {
            }

            [Disable(typeof(DisableProvider_Static))]
            public static void DisabledAtMethodLevel_CustomType_Static(
                [QueueTrigger("test")] string message)
            {
            }

            [Disable(typeof(DisableProvider_Instance))]
            public static void DisabledAtMethodLevel_CustomType_Instance(
                [QueueTrigger("test")] string message)
            {
            }

            [Disable("EnvironmentSettingTrue")]
            public static void DisabledByEnvironmentSetting(
                [QueueTrigger("test")] string message)
            {
            }

            [Disable("Disable_{MethodName}_%Test%")]
            public static void DisabledByAppSetting_BindingData(
                [QueueTrigger("test")] string message)
            {
            }

            public static void TestJob(
                [QueueTrigger("test")] string message)
            {
            }
        }

        [Disable("DisableSetting1")]
        public static class Functions2
        {
            public static void DisabledAtClassLevel(
                [QueueTrigger("test")] string message)
            {
            }
        }

        public class DisableProvider_Static
        {
            public static MethodInfo Method { get; set; }

            public static bool IsDisabled(MethodInfo method)
            {
                Method = method;
                return true;
            }
        }

        public class DisableProvider_Instance
        {
            public static MethodInfo Method { get; set; }

            public bool IsDisabled(MethodInfo method)
            {
                Method = method;
                return true;
            }
        }

        public class InvalidProvider_MissingFunction
        {
        }

        public class InvalidProvider_InvalidSignature
        {
            public static void IsDisabled(MethodInfo method)
            {
            }
        }
    }
}
