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
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Tests.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DotNetFunctionInvokerTests
    {
        public static IEnumerable<object[]> CompilationEnvironment
        {
            get
            {
                yield return new object[] { new Dictionary<string, string> { { EnvironmentSettingNames.CompilationReleaseMode, bool.TrueString } } };
                yield return new object[] { new Dictionary<string, string> { { EnvironmentSettingNames.CompilationReleaseMode, bool.FalseString } } };
            }
        }

        [Fact]
        public async Task ReloadScript_WithInvalidCompilationAndMissingMethod_ReportsResults()
        {
            // Create the compilation exception we expect to throw during the reload
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
                ScriptType = ScriptType.CSharp
            };

            metadata.Bindings.Add(new BindingMetadata() { Name = "Test", Type = "ManualTrigger" });

            var invoker = new DotNetFunctionInvoker(dependencies.Host.Object, metadata, new Collection<Script.Binding.FunctionBinding>(),
                new Collection<FunctionBinding>(), dependencies.EntrypointResolver.Object, dependencies.CompilationServiceFactory.Object);

            // Send file change notification to trigger a reload
            var fileEventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetTempPath(), Path.Combine(Path.GetFileName(rootFunctionsFolder), Path.GetFileName(filePath)));
            dependencies.Host.Object.EventManager.Publish(new FileEvent(EventSources.ScriptFiles, fileEventArgs));

            await TestHelpers.Await(() =>
            {
                IEnumerable<LogMessage> logMessages = dependencies.LoggerProvider.GetAllLogMessages();

                return logMessages.Any(t => t.FormattedMessage.Contains("Compilation failed.")) &&
                 logMessages.Any(t => t.FormattedMessage.Contains(DotNetConstants.MissingFunctionEntryPointCompilationCode));
            });

            dependencies.LoggerProvider.ClearAllLogMessages();

            CompilationErrorException resultException = await Assert.ThrowsAsync<CompilationErrorException>(() => invoker.GetFunctionTargetAsync());

            await TestHelpers.Await(() =>
            {
                IEnumerable<LogMessage> logMessages = dependencies.LoggerProvider.GetAllLogMessages();

                return logMessages.Any(t => t.FormattedMessage.Contains("Function compilation error")) &&
                 logMessages.Any(t => t.FormattedMessage.Contains(DotNetConstants.MissingFunctionEntryPointCompilationCode));
            });
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
                    ScriptType = ScriptType.CSharp
                };

                metadata.Bindings.Add(new BindingMetadata() { Name = "myQueueItem", Type = "ManualTrigger" });

                var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);

                var invoker = new DotNetFunctionInvoker(dependencies.Host.Object, metadata, new Collection<FunctionBinding>(),
                    new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(), new DotNetCompilationServiceFactory(null));

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

                // Create the invoker dependencies and setup the appropriate method to throw the exception
                RunDependencies dependencies = CreateDependencies();

                // Set the host to secondary
                dependencies.Host.SetupGet(h => h.IsPrimary).Returns(false);

                // Create a dummy file to represent our function
                string filePath = Path.Combine(rootFunctionsFolder, Guid.NewGuid().ToString() + ".csx");
                File.WriteAllText(filePath, Resources.TestFunctionWithMissingBindingArgumentsCode);

                var metadata = new FunctionMetadata
                {
                    ScriptFile = filePath,
                    FunctionDirectory = Path.GetDirectoryName(filePath),
                    Name = Guid.NewGuid().ToString(),
                    ScriptType = ScriptType.CSharp
                };

                metadata.Bindings.Add(new BindingMetadata() { Name = "myQueueItem", Type = "ManualTrigger" });

                var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);

                var invoker = new DotNetFunctionInvoker(dependencies.Host.Object, metadata, new Collection<FunctionBinding>(),
                    new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(), new DotNetCompilationServiceFactory(null));
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

            Collection<FunctionBinding> inputBindings = new Collection<FunctionBinding>()
            {
                TestHelpers.CreateTestBinding(new JObject
                {
                    { "type", "blobTrigger" },
                    { "name", "input" },
                    { "direction", "in" },
                    { "path", "test" }
                })
            };
            Collection<FunctionBinding> outputBindings = new Collection<FunctionBinding>()
            {
                TestHelpers.CreateTestBinding(new JObject
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
            // Create the invoker dependencies and setup the appropriate method to throw the exception
            RunDependencies dependencies = CreateDependencies();

            // Set the host to primary
            dependencies.Host.SetupGet(h => h.IsPrimary).Returns(true);

            var metadata = new FunctionMetadata
            {
                ScriptFile = "run.csx",
                FunctionDirectory = "c:\\somedir",
                Name = Guid.NewGuid().ToString(),
                ScriptType = ScriptType.CSharp
            };

            metadata.Bindings.Add(new BindingMetadata() { Name = "myQueueItem", Type = "ManualTrigger" });

            var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);

            var dotNetCompilation = new Mock<IDotNetCompilation>();
            var dotnetCompilationService = new Mock<ICompilationService<IDotNetCompilation>>();
            dotnetCompilationService.SetupSequence(s => s.GetFunctionCompilationAsync(It.IsAny<FunctionMetadata>()))
            .ThrowsAsync(new CompilationServiceException("1"))
            .ThrowsAsync(new CompilationServiceException("2"))
            .ThrowsAsync(new CompilationServiceException("3"))
            .ThrowsAsync(new CompilationServiceException("4")); // This should not be reached

            var compilationFactory = new Mock<ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver>>();
            compilationFactory.Setup(f => f.CreateService(ScriptType.CSharp, It.IsAny<IFunctionMetadataResolver>())).Returns(dotnetCompilationService.Object);

            var invoker = new DotNetFunctionInvoker(dependencies.Host.Object, metadata, new Collection<FunctionBinding>(),
                new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(),
                compilationFactory.Object, new Mock<IFunctionMetadataResolver>().Object);

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
                var environmentMock = new Mock<IScriptHostEnvironment>();

                // Create the invoker dependencies and setup the appropriate method to throw the exception
                RunDependencies dependencies = CreateDependencies(environment: environmentMock.Object);

                // Create a dummy file to represent our function
                string filePath = Path.Combine(tempDirectory.Path, Guid.NewGuid().ToString() + ".csx");
                File.WriteAllText(filePath, Resources.TestFunctionWithMissingBindingArgumentsCode);

                var metadata = new FunctionMetadata
                {
                    ScriptFile = filePath,
                    Name = Guid.NewGuid().ToString(),
                    ScriptType = ScriptType.CSharp,
                };

                metadata.Bindings.Add(new BindingMetadata { Type = "TestTrigger", Direction = BindingDirection.In });

                var metadataResolver = new Mock<IFunctionMetadataResolver>();
                metadataResolver.Setup(r => r.RestorePackagesAsync())
                    .ReturnsAsync(new PackageRestoreResult { IsInitialInstall = initialInstall, ReferencesChanged = referencesChanged });

                var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);

                var invoker = new DotNetFunctionInvoker(dependencies.Host.Object, metadata, new Collection<FunctionBinding>(),
                  new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(),
                  new DotNetCompilationServiceFactory(null), metadataResolver.Object);

                await invoker.RestorePackagesAsync(true);

                // Delay the check as the shutdown call is debounced
                // and won't be made immediately
                await Task.Delay(1000);

                environmentMock.Verify(e => e.Shutdown(), Times.Exactly(shutdownExpected ? 1 : 0));
            }
        }

        private RunDependencies CreateDependencies(IScriptHostEnvironment environment = null)
        {
            var dependencies = new RunDependencies();

            var scriptHostConfiguration = new ScriptHostConfiguration
            {
                HostConfig = new JobHostConfiguration(),
                FileLoggingMode = FileLoggingMode.Always,
                FileWatchingEnabled = true
            };

            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            scriptHostConfiguration.HostConfig.LoggerFactory = loggerFactory;

            var eventManager = new ScriptEventManager();

            var host = new Mock<ScriptHost>(environment ?? new NullScriptHostEnvironment(), eventManager, scriptHostConfiguration, null, null, null);
            host.CallBase = true;

            host.SetupGet(h => h.IsPrimary).Returns(true);
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
            compilationServiceFactory.Setup(f => f.CreateService(ScriptType.CSharp, It.IsAny<IFunctionMetadataResolver>()))
                .Returns(compilationService.Object);

            var metricsLogger = new MetricsLogger();
            scriptHostConfiguration.HostConfig.AddService<IMetricsLogger>(metricsLogger);

            return new RunDependencies
            {
                Host = host,
                EntrypointResolver = entrypointResolver,
                Compilation = compilation,
                CompilationService = compilationService,
                CompilationServiceFactory = compilationServiceFactory,
                LoggerProvider = loggerProvider
            };
        }

        private class RunDependencies
        {
            public Mock<ScriptHost> Host { get; set; }

            public Mock<IFunctionEntryPointResolver> EntrypointResolver { get; set; }

            public Mock<IDotNetCompilation> Compilation { get; set; }

            public Mock<ICompilationService<IDotNetCompilation>> CompilationService { get; set; }

            public Mock<ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver>> CompilationServiceFactory { get; set; }

            public TestLoggerProvider LoggerProvider { get; set; }
        }
    }
}
