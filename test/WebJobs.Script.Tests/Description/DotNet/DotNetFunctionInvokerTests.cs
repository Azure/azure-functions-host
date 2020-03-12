// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Tests.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DotNetFunctionInvokerTests : IDisposable
    {
        private IHost _host;
        private ScriptHost _scriptHost;

        public static IEnumerable<object[]> CompilationEnvironment
        {
            get
            {
                yield return new object[] { new Dictionary<string, string> { { EnvironmentSettingNames.CompilationReleaseMode, bool.TrueString } } };
                yield return new object[] { new Dictionary<string, string> { { EnvironmentSettingNames.CompilationReleaseMode, bool.FalseString } } };
            }
        }

        [Fact]
        public async Task GetFunctionTargetAsync_CompilationError_ReportsResults()
        {
            // Create the compilation exception we expect to throw
            var descriptor = new DiagnosticDescriptor(DotNetConstants.MissingFunctionEntryPointCompilationCode,
                "Test compilation exception", "Test compilation error", "AzureFunctions", DiagnosticSeverity.Error, true);
            var exception = new CompilationErrorException("Test compilation exception", ImmutableArray.Create(Diagnostic.Create(descriptor, Location.None)));

            // Create the invoker dependencies and setup the appropriate method to throw the exception
            RunDependencies dependencies = CreateDependencies();
            dependencies.Compilation.Setup(c => c.GetEntryPointSignature(It.IsAny<IFunctionEntryPointResolver>(), It.IsAny<Assembly>()))
                .Throws(exception);
            dependencies.Compilation.Setup(c => c.EmitAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(DotNetCompilationResult.FromPath(typeof(DotNetFunctionInvokerTests).Assembly.Location));

            string functionName = Guid.NewGuid().ToString();
            string rootFunctionsFolder = Path.Combine(Path.GetTempPath(), functionName);
            Directory.CreateDirectory(rootFunctionsFolder);

            // Create a dummy file to represent our function
            string filePath = Path.Combine(rootFunctionsFolder, Guid.NewGuid().ToString() + ".csx");
            File.WriteAllText(filePath, string.Empty);

            var metadata = new FunctionMetadata
            {
                ScriptFile = filePath,
                FunctionDirectory = Path.GetDirectoryName(filePath),
                Name = functionName,
                Language = DotNetScriptTypes.CSharp
            };

            metadata.Bindings.Add(new BindingMetadata() { Name = "Test", Type = "ManualTrigger" });

            var invoker = new DotNetFunctionInvoker(dependencies.Host, metadata, new Collection<FunctionBinding>(), new Collection<FunctionBinding>(),
                dependencies.EntrypointResolver.Object, dependencies.CompilationServiceFactory.Object, dependencies.LoggerFactory, dependencies.MetricsLogger,
                new Collection<IScriptBindingProvider>());

            // Send file change notification to trigger a reload
            var fileEventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetTempPath(), Path.Combine(Path.GetFileName(rootFunctionsFolder), Path.GetFileName(filePath)));
            dependencies.Host.EventManager.Publish(new FileEvent(EventSources.ScriptFiles, fileEventArgs));

            LogMessage[] logMessages = null;
            var loggerProvider = dependencies.LoggerProvider;
            await TestHelpers.Await(() =>
            {
                logMessages = loggerProvider.GetAllLogMessages().ToArray();

                return logMessages.Any(t => t.FormattedMessage.Contains("Compilation failed.")) &&
                    logMessages.Any(t => t.FormattedMessage.Contains(DotNetConstants.MissingFunctionEntryPointCompilationCode));
            });

            // verify expected logs when the function target is retrieved
            // NOT on the invocation path
            dependencies.LoggerProvider.ClearAllLogMessages();
            await Assert.ThrowsAsync<CompilationErrorException>(async () =>
            {
                await invoker.GetFunctionTargetAsync();
            });
            Assert.Equal(3, logMessages.Length);
            Assert.Equal($"Script for function '{functionName}' changed. Reloading.", logMessages[0].FormattedMessage);
            Assert.Equal(LogLevel.Information, logMessages[0].Level);
            Assert.Equal("error AF001: Test compilation error", logMessages[1].FormattedMessage);
            Assert.Equal(LogLevel.Error, logMessages[1].Level);
            Assert.True(logMessages[1].State.Any(q => q.Key == ScriptConstants.LogPropertyIsUserLogKey));
            Assert.Equal("Compilation failed.", logMessages[2].FormattedMessage);
            Assert.Equal(LogLevel.Information, logMessages[2].Level);
            Assert.True(logMessages.All(p => p.State.Any(q => q.Key == ScriptConstants.LogPropertyPrimaryHostKey)));

            await TestHelpers.Await(() =>
            {
                logMessages = loggerProvider.GetAllLogMessages().ToArray();

                return logMessages.Any(t => t.FormattedMessage.Contains("Function compilation error")) &&
                    logMessages.Any(t => t.FormattedMessage.Contains(DotNetConstants.MissingFunctionEntryPointCompilationCode));
            });

            // now test the invoke path and verify that the compilation error
            // details are written
            loggerProvider.ClearAllLogMessages();
            await Assert.ThrowsAsync<CompilationErrorException>(async () =>
            {
                await invoker.GetFunctionTargetAsync(isInvocation: true);
            });
            logMessages = loggerProvider.GetAllLogMessages().ToArray();
            Assert.Equal(2, logMessages.Length);
            Assert.Equal("Function compilation error", logMessages[0].FormattedMessage);
            Assert.Equal(LogLevel.Error, logMessages[0].Level);
            Assert.Same("Test compilation exception", logMessages[0].Exception.Message);
            Assert.Equal("error AF001: Test compilation error", logMessages[1].FormattedMessage);
            Assert.Equal(LogLevel.Error, logMessages[1].Level);
            Assert.True(logMessages[1].State.Any(q => q.Key == ScriptConstants.LogPropertyIsUserLogKey));
            Assert.True(logMessages.All(p => !(p.State != null && p.State.Any(q => q.Key == ScriptConstants.LogPropertyPrimaryHostKey))));
        }

        [Theory]
        [MemberData(nameof(CompilationEnvironment))]
        public async Task Compilation_WithMissingBindingArguments_LogsAF004Warning(IDictionary<string, string> environment)
        {
            using (var testEnvironment = new TestScopedEnvironmentVariable(environment))
            {
                // Create the compilation exception we expect to throw during the reload
                string rootFunctionsFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(rootFunctionsFolder);

                // Create the invoker dependencies and setup the appropriate method to throw the exception
                RunDependencies dependencies = CreateDependencies();

                // Create a dummy file to represent our function
                string filePath = Path.Combine(rootFunctionsFolder, Guid.NewGuid().ToString() + ".csx");
                File.WriteAllText(filePath, Resources.TestFunctionWithMissingBindingArgumentsCode);

                var metadata = new FunctionMetadata
                {
                    ScriptFile = filePath,
                    FunctionDirectory = Path.GetDirectoryName(filePath),
                    Name = Guid.NewGuid().ToString(),
                    Language = DotNetScriptTypes.CSharp
                };

                metadata.Bindings.Add(new BindingMetadata() { Name = "myQueueItem", Type = "ManualTrigger" });

                var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);

                var invoker = new DotNetFunctionInvoker(dependencies.Host, metadata, new Collection<FunctionBinding>(),
                    new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(), new DotNetCompilationServiceFactory(null),
                    dependencies.LoggerFactory, dependencies.MetricsLogger, new Collection<IScriptBindingProvider>());

                try
                {
                    await invoker.GetFunctionTargetAsync();
                }
                catch (CompilationErrorException exc)
                {
                    var builder = new StringBuilder();
                    builder.AppendLine(Resources.TestFunctionWithMissingBindingArgumentsCode);
                    builder.AppendLine();

                    string compilationDetails = exc.Diagnostics.Aggregate(
                        builder,
                        (a, d) => a.AppendLine(d.ToString()),
                        a => a.ToString());

                    throw new Exception(compilationDetails, exc);
                }

                Assert.Contains(dependencies.LoggerProvider.GetAllLogMessages(),
                    t => t.FormattedMessage.Contains($"warning {DotNetConstants.MissingBindingArgumentCompilationCode}") && t.FormattedMessage.Contains("'TestBinding'"));
            }
        }

        [Theory]
        [MemberData(nameof(CompilationEnvironment))]
        public async Task Compilation_OnSecondaryHost_SuppressesLogs(IDictionary<string, string> environment)
        {
            using (new TestScopedEnvironmentVariable(environment))
            {
                // Create the compilation exception we expect to throw during the reload
                string rootFunctionsFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(rootFunctionsFolder);

                // Set the host to secondary
                var stateProviderMock = new Mock<IPrimaryHostStateProvider>();
                stateProviderMock.Setup(m => m.IsPrimary).Returns(false);

                // Create the invoker dependencies and setup the appropriate method to throw the exception
                RunDependencies dependencies = CreateDependencies(configureServices: s => { s.AddSingleton(stateProviderMock.Object); });

                // Create a dummy file to represent our function
                string filePath = Path.Combine(rootFunctionsFolder, Guid.NewGuid().ToString() + ".csx");
                File.WriteAllText(filePath, Resources.TestFunctionWithMissingBindingArgumentsCode);

                var metadata = new FunctionMetadata
                {
                    ScriptFile = filePath,
                    FunctionDirectory = Path.GetDirectoryName(filePath),
                    Name = Guid.NewGuid().ToString(),
                    Language = DotNetScriptTypes.CSharp
                };

                metadata.Bindings.Add(new BindingMetadata() { Name = "myQueueItem", Type = "ManualTrigger" });

                var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);

                var invoker = new DotNetFunctionInvoker(dependencies.Host, metadata, new Collection<FunctionBinding>(),
                    new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(), new DotNetCompilationServiceFactory(null),
                    dependencies.LoggerFactory, dependencies.MetricsLogger, new Collection<IScriptBindingProvider>());
                try
                {
                    await invoker.GetFunctionTargetAsync();
                }
                catch (CompilationErrorException exc)
                {
                    var builder = new StringBuilder();
                    builder.AppendLine(Resources.TestFunctionWithMissingBindingArgumentsCode);
                    builder.AppendLine();

                    string compilationDetails = exc.Diagnostics.Aggregate(
                        builder,
                        (a, d) => a.AppendLine(d.ToString()),
                        a => a.ToString());

                    throw new Exception(compilationDetails, exc);
                }

                // Verify that we send the log, but that it has MS_PrimaryHost set so the logger can filter appropriately.
                var logMessage = dependencies.LoggerProvider.GetAllLogMessages().Single();
                Assert.True((bool)logMessage.State.Single(k => k.Key == ScriptConstants.LogPropertyPrimaryHostKey).Value);
            }
        }

        [Fact]
        public void ValidateFunctionBindingArguments_ReturnBinding_Succeeds()
        {
            Collection<FunctionParameter> parameters = new Collection<FunctionParameter>()
            {
                new FunctionParameter("input", "String", false, RefKind.None)
            };
            FunctionSignature signature = new FunctionSignature("Test", "Test", ImmutableArray.CreateRange<FunctionParameter>(parameters), "Test", false);

            var host = new HostBuilder().ConfigureDefaultTestWebScriptHost(b =>
            {
                b.AddAzureStorage();
            }).Build();

            Collection<FunctionBinding> inputBindings = new Collection<FunctionBinding>()
            {
                TestHelpers.CreateBindingFromHost(host, new JObject
                {
                    { "type", "blobTrigger" },
                    { "name", "input" },
                    { "direction", "in" },
                    { "path", "test" }
                })
            };

            Collection<FunctionBinding> outputBindings = new Collection<FunctionBinding>()
            {
                TestHelpers.CreateBindingFromHost(host, new JObject
                {
                    { "type", "blob" },
                    { "name", ScriptConstants.SystemReturnParameterBindingName },
                    { "direction", "out" },
                    { "path", "test/test" }
                })
            };

            var diagnostics = DotNetFunctionInvoker.ValidateFunctionBindingArguments(signature, "input", inputBindings, outputBindings);
            Assert.Equal(0, diagnostics.Count());
        }

        [Fact]
        public async Task CompilerError_IsRetried_UpToLimit()
        {
            // Set the host to primary
            var stateProviderMock = new Mock<IPrimaryHostStateProvider>();
            stateProviderMock.Setup(m => m.IsPrimary).Returns(false);

            // Create the invoker dependencies and setup the appropriate method to throw the exception
            RunDependencies dependencies = CreateDependencies(configureServices: s => { s.AddSingleton(stateProviderMock.Object); });

            var metadata = new FunctionMetadata
            {
                ScriptFile = "run.csx",
                FunctionDirectory = "c:\\somedir",
                Name = Guid.NewGuid().ToString(),
                Language = DotNetScriptTypes.CSharp
            };

            metadata.Bindings.Add(new BindingMetadata() { Name = "myQueueItem", Type = "ManualTrigger" });

            var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);

            var dotNetCompilation = new Mock<IDotNetCompilation>();
            var dotnetCompilationService = new Mock<ICompilationService<IDotNetCompilation>>();
            dotnetCompilationService
                .SetupSequence(s => s.GetFunctionCompilationAsync(It.IsAny<FunctionMetadata>()))
                .ThrowsAsync(new CompilationServiceException("1"))
                .ThrowsAsync(new CompilationServiceException("2"))
                .ThrowsAsync(new CompilationServiceException("3"))
                .ThrowsAsync(new CompilationServiceException("4")); // This should not be reached

            var compilationFactory = new Mock<ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver>>();
            compilationFactory
                .Setup(f => f.CreateService(DotNetScriptTypes.CSharp, It.IsAny<IFunctionMetadataResolver>()))
                .Returns(dotnetCompilationService.Object);

            var invoker = new DotNetFunctionInvoker(dependencies.Host, metadata, new Collection<FunctionBinding>(),
                new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(),
                compilationFactory.Object, dependencies.LoggerFactory, dependencies.MetricsLogger, new Collection<IScriptBindingProvider>(), new Mock<IFunctionMetadataResolver>().Object);

            var arguments = new object[]
            {
                new ExecutionContext()
                {
                    FunctionDirectory = "c:\\test",
                    FunctionName = "test",
                    InvocationId = Guid.NewGuid()
                }
            };

            for (int i = 1; i <= 10; i++)
            {
                var expectedAttempt = Math.Min(i, 3);
                CompilationServiceException exception = await Assert.ThrowsAsync<CompilationServiceException>(() => invoker.Invoke(arguments));
                Assert.Equal(expectedAttempt.ToString(), exception.Message);
            }

            var compilerErrorTraces = dependencies.LoggerProvider.GetAllLogMessages()
                .Where(t => string.Equals(t.FormattedMessage, "Function loader reset. Failed compilation result will not be cached."));

            // 3 attempts total, make sure we've logged the 2 retries.
            Assert.Equal(2, compilerErrorTraces.Count());
        }

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        public async Task RestorePackagesAsync_WithUpdatedReferences_TriggersShutdown(bool initialInstall, bool referencesChanged, bool shutdownExpected)
        {
            using (var tempDirectory = new TempDirectory())
            {
                var mockApplicationLifetime = new Mock<IApplicationLifetime>();

                // Create the invoker dependencies and setup the appropriate method to throw the exception
                RunDependencies dependencies = CreateDependencies(s =>
                {
                    s.AddSingleton(mockApplicationLifetime.Object);
                });

                // Create a dummy file to represent our function
                string filePath = Path.Combine(tempDirectory.Path, Guid.NewGuid().ToString() + ".csx");
                File.WriteAllText(filePath, Resources.TestFunctionWithMissingBindingArgumentsCode);

                var metadata = new FunctionMetadata
                {
                    ScriptFile = filePath,
                    Name = Guid.NewGuid().ToString(),
                    Language = DotNetScriptTypes.CSharp,
                };

                metadata.Bindings.Add(new BindingMetadata { Type = "TestTrigger", Direction = BindingDirection.In });

                var metadataResolver = new Mock<IFunctionMetadataResolver>();
                metadataResolver.Setup(r => r.RestorePackagesAsync())
                    .ReturnsAsync(new PackageRestoreResult { IsInitialInstall = initialInstall, ReferencesChanged = referencesChanged });

                var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);

                var invoker = new DotNetFunctionInvoker(dependencies.Host, metadata, new Collection<FunctionBinding>(),
                  new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(),
                  new DotNetCompilationServiceFactory(null), dependencies.LoggerFactory, dependencies.MetricsLogger, new Collection<IScriptBindingProvider>(), metadataResolver.Object);

                await invoker.RestorePackagesAsync(true);

                // Delay the check as the shutdown call is debounced
                // and won't be made immediately
                await Task.Delay(1000);

                mockApplicationLifetime.Verify(e => e.StopApplication(), Times.Exactly(shutdownExpected ? 1 : 0));
            }
        }

        // TODO: DI (FACAVAL) Use test helpers to create host and inject services
        private RunDependencies CreateDependencies(Action<IServiceCollection> configureServices = null)
        {
            var dependencies = new RunDependencies();

            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var eventManager = new ScriptEventManager();
            var entrypointResolver = new Mock<IFunctionEntryPointResolver>();

            var compilation = new Mock<IDotNetCompilation>();
            compilation.Setup(c => c.GetDiagnostics())
                .Returns(ImmutableArray<Diagnostic>.Empty);

            var compilationService = new Mock<ICompilationService<IDotNetCompilation>>();
            compilationService.Setup(s => s.SupportedFileTypes)
                .Returns(() => new[] { ".csx" });
            compilationService.Setup(s => s.GetFunctionCompilationAsync(It.IsAny<FunctionMetadata>()))
                .ReturnsAsync(compilation.Object);

            var compilationServiceFactory = new Mock<ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver>>();
            compilationServiceFactory.Setup(f => f.CreateService(DotNetScriptTypes.CSharp, It.IsAny<IFunctionMetadataResolver>()))
                .Returns(compilationService.Object);

            var hostBuilder = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(o =>
                {
                    o.ScriptPath = TestHelpers.FunctionsTestDirectory;
                    o.LogPath = TestHelpers.GetHostLogFileDirectory().Parent.FullName;
                });

            if (configureServices != null)
            {
                hostBuilder.ConfigureServices(configureServices);
            }

            _host = hostBuilder.Build();

            _scriptHost = _host.GetScriptHost();

            return new RunDependencies
            {
                Host = _scriptHost,
                EntrypointResolver = entrypointResolver,
                Compilation = compilation,
                CompilationService = compilationService,
                CompilationServiceFactory = compilationServiceFactory,
                LoggerProvider = loggerProvider,
                LoggerFactory = loggerFactory,
                MetricsLogger = new TestMetricsLogger()
            };
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scriptHost?.Dispose();
                _host?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private class RunDependencies
        {
            public ScriptHost Host { get; set; }

            public Mock<IFunctionEntryPointResolver> EntrypointResolver { get; set; }

            public Mock<IDotNetCompilation> Compilation { get; set; }

            public Mock<ICompilationService<IDotNetCompilation>> CompilationService { get; set; }

            public Mock<ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver>> CompilationServiceFactory { get; set; }

            public TestLoggerProvider LoggerProvider { get; set; }

            public ILoggerFactory LoggerFactory { get; set; }

            public IMetricsLogger MetricsLogger { get; set; }
        }
    }
}
