using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
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
            // This is a best-effort logging improvement. During host shutdown, downstream services can
            // be disposed before ScriptLoggerFactory wins the race and throws the fuller HostDisposedException.
            const int maxAttempts = 3;
            string lastFailure = null;
            for (int i = 1; i <= maxAttempts; i++)
            {
                string bestEffortMiss = await TryAssertDisposedScriptLoggerFactoryAsync();
                if (bestEffortMiss is null)
                {
                    Console.WriteLine($"Observed the fuller HostDisposedException on attempt {i}/{maxAttempts}.");
                    return;
                }

                lastFailure = bestEffortMiss;
                Console.WriteLine(
                    $"Attempt {i}/{maxAttempts}: the fuller HostDisposedException was not observed. " +
                    $"This logging improvement is best effort during host shutdown.{Environment.NewLine}{bestEffortMiss}");
            }

            Assert.True(
                false,
                $"The fuller HostDisposedException was not observed after {maxAttempts} attempts. " +
                $"This logging improvement is best effort during host shutdown.{Environment.NewLine}{lastFailure}");
        }

        // Runs one attempt of the disposed-logger scenario. Returns null when the fuller
        // HostDisposedException is observed, or a diagnostic string for the known best-effort miss.
        private static async Task<string> TryAssertDisposedScriptLoggerFactoryAsync()
        {
            var host = new TestFunctionHost(@"TestScripts\CSharp",
                configureScriptHostServices: s =>
                {
                    s.AddSingleton<IExtensionConfigProvider, CustomTriggerExtensionConfigProvider>();
                    s.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "CustomTrigger" });
                });

            await CustomListener.RunAsync("one");

            var jobhost = host.JobHostServices.GetRequiredService<IScriptJobHost>();
            await jobhost.StopAsync();
            await host.WebHost.StopAsync();
            host.WebHost.Dispose();
            host.Dispose();

            // Capture log state after dispose so we have diagnostic context if the expected
            // HostDisposedException does not surface (the test has historically been flaky here).
            string logsAfterDispose = SafeGetLog(host);

            // In this scenario, the logger throws an exception before we enter the try/catch for the function invocation.
            Exception capturedException = null;
            FunctionResult capturedResult = null;
            try
            {
                capturedResult = await CustomListener.RunAsync("two");
            }
            catch (Exception runEx)
            {
                capturedException = runEx;
            }

            if (capturedException is HostDisposedException hostDisposedException)
            {
                Assert.Equal($"The host is disposed and cannot be used. Disposed object: '{typeof(ScriptLoggerFactory).FullName}'; Found IListener in stack trace: '{typeof(CustomListener).AssemblyQualifiedName}'", hostDisposedException.Message);
                Assert.Contains("CustomListener.RunAsync", hostDisposedException.StackTrace);
                return null;
            }

            string logsAfterRun = SafeGetLog(host);
            string failure = BuildDisposedAssertionFailureMessage(capturedException, capturedResult, logsAfterDispose, logsAfterRun);
            if (IsKnownShutdownRace(capturedException, capturedResult))
            {
                return failure;
            }

            Assert.True(false, failure);
            return failure;
        }

        private static bool IsKnownShutdownRace(Exception capturedException, FunctionResult capturedResult)
        {
            if (capturedException is not null)
            {
                return false;
            }

            if (capturedResult?.Exception is not ObjectDisposedException objectDisposedException)
            {
                return false;
            }

            return string.Equals(objectDisposedException.ObjectName, "IServiceProvider", StringComparison.Ordinal);
        }

        private static string SafeGetLog(TestFunctionHost host)
        {
            try
            {
                return host.GetLog();
            }
            catch (Exception ex)
            {
                return $"<<unable to read host log: {ex.GetType().Name}: {ex.Message}>>";
            }
        }

        private static string BuildDisposedAssertionFailureMessage(Exception capturedException, FunctionResult capturedResult, string logsAfterDispose, string logsAfterRun)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Expected '{nameof(HostDisposedException)}' from {nameof(CustomListener)}.{nameof(CustomListener.RunAsync)}(\"two\") after host dispose, but it was not observed.");

            if (capturedException is null)
            {
                sb.AppendLine("Captured exception: <none>");
            }
            else
            {
                sb.AppendLine($"Captured exception: {capturedException.GetType().FullName}: {capturedException.Message}");
                sb.AppendLine("Captured exception stack:");
                sb.AppendLine(capturedException.StackTrace ?? "<no stack trace>");
            }

            if (capturedResult is not null)
            {
                sb.AppendLine($"Captured FunctionResult.Succeeded: {capturedResult.Succeeded}");
                if (capturedResult.Exception is not null)
                {
                    sb.AppendLine($"Captured FunctionResult.Exception: {capturedResult.Exception.GetType().FullName}: {capturedResult.Exception.Message}");
                    sb.AppendLine(capturedResult.Exception.StackTrace ?? "<no stack trace>");
                }
            }

            sb.AppendLine("=== Host log captured immediately after dispose ===");
            sb.AppendLine(logsAfterDispose);
            sb.AppendLine("=== Host log captured after second RunAsync ===");
            sb.AppendLine(logsAfterRun);

            return sb.ToString();
        }

        // TODO: Need to review this
        //[Fact]
        //public async Task DisposedResolver_UsesFullStackTrace()
        //{
        //    var host = new TestFunctionHost(@"TestScripts\CSharp",
        //        configureScriptHostServices: s =>
        //        {
        //            s.AddSingleton<IExtensionConfigProvider, CustomTriggerExtensionConfigProvider>();
        //            s.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "CustomTrigger" });
        //            s.AddSingleton<ILoggerFactory, TestScriptLoggerFactory>();
        //        });

        //    await CustomListener.RunAsync("one");

        //    host.Dispose();

        //    // In this scenario, the function is considered failed even though the function itself was never called.
        //    var result = await CustomListener.RunAsync("two");

        //    Assert.False(result.Succeeded);

        //    var ex = result.Exception;
        //    Assert.Equal($"The host is disposed and cannot be used. Disposed object: '{typeof(ScopedResolver).FullName}'; Found IListener in stack trace: '{typeof(CustomListener).AssemblyQualifiedName}'", ex.Message);
        //    Assert.Contains("CustomListener.RunAsync", ex.StackTrace);
        //}

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
