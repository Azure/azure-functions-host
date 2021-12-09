using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.Script.Tests.EndToEnd;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(CSharpEndToEndTests))]
    public class HostDisposedExceptionTests
    {
        [Fact]
        public async Task DisposedScriptLoggerFactory_UsesFullStackTrace()
        {
            var host = new TestFunctionHost(@"TestScripts\CSharp",
                configureScriptHostServices: s =>
                {
                    s.AddSingleton<IExtensionConfigProvider, CustomTriggerExtensionConfigProvider>();
                    s.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "CustomTrigger" });
                });

            await CustomListener.RunAsync("one");

            host.Dispose();

            // In this scenario, the logger throws an exception before we enter the try/catch for the function invocation.
            var ex = await Assert.ThrowsAsync<HostDisposedException>(() => CustomListener.RunAsync("two"));

            Assert.Equal($"The host is disposed and cannot be used. Disposed object: '{typeof(ScriptLoggerFactory).FullName}'; Found IListener in stack trace: '{typeof(CustomListener).AssemblyQualifiedName}'", ex.Message);
            Assert.Contains("CustomListener.RunAsync", ex.StackTrace);
        }

        [Fact]
        public async Task DisposedResolver_UsesFullStackTrace()
        {
            var host = new TestFunctionHost(@"TestScripts\CSharp",
                configureScriptHostServices: s =>
                {
                    s.AddSingleton<IExtensionConfigProvider, CustomTriggerExtensionConfigProvider>();
                    s.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "CustomTrigger" });
                    s.AddSingleton<ILoggerFactory, TestScriptLoggerFactory>();
                });

            await CustomListener.RunAsync("one");

            host.Dispose();

            // In this scenario, the function is considered failed even though the function itself was never called.
            var result = await CustomListener.RunAsync("two");

            Assert.False(result.Succeeded);

            var ex = result.Exception;

            // Previous:
            //Assert.Equal($"The host is disposed and cannot be used. Disposed object: '{typeof(NotImplementedException).FullName}'; Found IListener in stack trace: '{typeof(CustomListener).AssemblyQualifiedName}'", ex.Message);

            // TODO: This doesn't have the HostDisposedException we caught *specifically for CreateScope()*
            //    Do we want to repalce the scope provider at the host level specifically for capturing this? We'd need to intercept .CreateScope() for equivalent behavior here
            //    ...but there's also the question if _that's_ where we'd throw when disposing a whole host while continuing to run things. If we're just making sure "we're disposed"
            //    ...then this works, but if we want a more friendly error wrapper to indicate it's the host disposal we happened to capture on *this path* (only ensured in a controlled test)
            //    ...then we would need to go back to wrapping the IServiceScopeFactory (globaly) and best-guess catching exceptions, e.g. an exception filter for the below.
            Assert.Equal("Cannot access a disposed object.\r\nObject name: 'IServiceProvider'.", ex.Message);

            // TODO: This also changes, because we're going to go boom before executing the function, and our stack trace is after a yield so we're coming off the task pool
            //Assert.Contains("CustomListener.RunAsync", ex.StackTrace);
        }

        [Fact]
        public void Serialization()
        {
            HostDisposedException originalEx = new HostDisposedException("someObject", new ObjectDisposedException("someObject"));
            HostDisposedException deserializedEx;

            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
#pragma warning disable SYSLIB0011 // Type or member is obsolete
                bf.Serialize(ms, originalEx);
                ms.Seek(0, 0);
                deserializedEx = (HostDisposedException)bf.Deserialize(ms);
#pragma warning restore SYSLIB0011 // Type or member is obsolete
            }

            Assert.Equal(originalEx.ToString(), deserializedEx.ToString());
        }

        private class TestScriptLoggerFactory : ScriptLoggerFactory
        {
            public static bool ShouldWait { get; set; } = false;

            public TestScriptLoggerFactory(IEnumerable<ILoggerProvider> providers, IOptionsMonitor<LoggerFilterOptions> filterOption)
                : base(providers, filterOption)
            {
            }

            internal override ILogger CreateLoggerInternal(string categoryName)
            {
                try
                {
                    return base.CreateLoggerInternal(categoryName);
                }
                catch (HostDisposedException)
                {
                    // Simulate the race where the logger succeeds and later the container fails.
                    return NullLogger.Instance;
                }
            }
        }

        [Binding]
        public class CustomTriggerAttribute : Attribute
        {
        }

        private class CustomTriggerExtensionConfigProvider : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var rule = context.AddBindingRule<CustomTriggerAttribute>();
                rule.BindToTrigger<string>(new CustomTriggerAttributeBindingProvider());
            }
        }

        private class CustomTriggerAttributeBindingProvider : ITriggerBindingProvider
        {
            public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
            {
                ParameterInfo parameter = context.Parameter;
                CustomTriggerAttribute attribute = parameter.GetCustomAttribute<CustomTriggerAttribute>(inherit: false);
                if (attribute == null)
                {
                    return Task.FromResult<ITriggerBinding>(null);
                }

                return Task.FromResult<ITriggerBinding>(new CustomTriggerBinding());
            }
        }

        private class CustomTriggerBinding : ITriggerBinding
        {
            private readonly IReadOnlyDictionary<string, object> _emptyBindingData = new Dictionary<string, object>();

            public Type TriggerValueType => typeof(string);

            public IReadOnlyDictionary<string, Type> BindingDataContract { get; } = new Dictionary<string, Type>();

            public Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
            {
                return Task.FromResult<ITriggerData>(new TriggerData(null, _emptyBindingData));
            }

            public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
            {
                return Task.FromResult<IListener>(new CustomListener(context.Executor));
            }

            public ParameterDescriptor ToParameterDescriptor()
            {
                return new ParameterDescriptor();
            }
        }

        private class CustomListener : IListener
        {
            private static ITriggeredFunctionExecutor _executor;

            public CustomListener(ITriggeredFunctionExecutor executor)
            {
                _executor = executor;
            }

            public void Cancel()
            {
            }

            public static Task<FunctionResult> RunAsync(string input)
            {
                return _executor.TryExecuteAsync(new TriggeredFunctionData() { TriggerValue = input }, CancellationToken.None);
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }
}
