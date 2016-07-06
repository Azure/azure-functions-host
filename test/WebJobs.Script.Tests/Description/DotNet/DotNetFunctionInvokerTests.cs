// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Tests.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.DotNet
{
    public class DotNetFunctionInvokerTests
    {
        [Fact]
        public async Task ReloadScript_WithInvalidCompilationAndMissingMethod_ReportsResults()
        {
            // Create the compilation exception we expect to throw during the reload
            var descriptor = new DiagnosticDescriptor(DotNetConstants.MissingFunctionEntryPointCompilationCode, 
                "Test compilation exception", "Test compilation error", "AzureFunctions", DiagnosticSeverity.Error, true);
            var exception = new CompilationErrorException("Test compilation exception", ImmutableArray.Create(Diagnostic.Create(descriptor, Location.None)));

            // Create the invoker dependencies and setup the appropriate method to throw the exception
            RunDependencies dependencies = CreateDependencies();
            dependencies.Compilation.Setup(c => c.GetEntryPointSignature(It.IsAny<IFunctionEntryPointResolver>()))
             .Throws(exception);

            string rootFunctionsFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(rootFunctionsFolder);

            // Create a dummy file to represent our function
            string filePath = Path.Combine(rootFunctionsFolder, Guid.NewGuid().ToString() + ".csx");
            File.WriteAllText(filePath, string.Empty);

            var metadata = new FunctionMetadata
            {
                ScriptFile = filePath,
                Name = Guid.NewGuid().ToString(),
                ScriptType = ScriptType.CSharp
            };

            metadata.Bindings.Add(new BindingMetadata() { Name = "Test", Type = "ManualTrigger" });

            var invoker = new DotNetFunctionInvoker(dependencies.Host.Object, metadata, new Collection<Script.Binding.FunctionBinding>(),
                new Collection<Script.Binding.FunctionBinding>(), dependencies.EntrypointResolver.Object, new FunctionAssemblyLoader(string.Empty), 
                dependencies.CompilationServiceFactory.Object);

            // Update the file to trigger a reload
            File.WriteAllText(filePath, string.Empty);            

            // Verify that our expected messages were logged, including the compilation result
            await TestHelpers.Await(() => 
            {
                Collection<string> logs = TestHelpers.GetFunctionLogsAsync(metadata.Name, false).Result;
                if (logs != null)
                {
                    return logs.Any(s => s.Contains("Compilation failed.")) && logs.Any(s => s.Contains(DotNetConstants.MissingFunctionEntryPointCompilationCode));
                }

                return false;
            }, 10 * 1000);
        }

        [Fact]
        public async Task Compilation_WithMissingBindingArguments_LogsAF004Warning()
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
                Name = Guid.NewGuid().ToString(),
                ScriptType = ScriptType.CSharp
            };

            metadata.Bindings.Add(new BindingMetadata() { Name = "myQueueItem", Type = "ManualTrigger" });

            var testBinding = new Mock<FunctionBinding>(null, new BindingMetadata() { Name = "TestBinding", Type = "blob" }, FileAccess.Write);
            
            var invoker = new DotNetFunctionInvoker(dependencies.Host.Object, metadata, new Collection<FunctionBinding>(),
                new Collection<FunctionBinding> { testBinding.Object }, new FunctionEntryPointResolver(), new FunctionAssemblyLoader(string.Empty),
                new DotNetCompilationServiceFactory());

            await invoker.GetFunctionTargetAsync();

            // Verify that our expected messages were logged, including the compilation result
            await TestHelpers.Await(() =>
            {
                Collection<string> logs = TestHelpers.GetFunctionLogsAsync(metadata.Name, false).Result;
                if (logs != null)
                {
                    //Check that our warning diagnostic was logged (e.g. "warning AF004: Missing binding argument named 'TestBinding'.");
                    return logs.Any(s => s.Contains($"warning {DotNetConstants.MissingBindingArgumentCompilationCode}") && s.Contains("'TestBinding'"));
                }

                return false;
            }, 10 * 1000);
        }

        private RunDependencies CreateDependencies()
        {
            var dependencies = new RunDependencies();

            var traceWriter = new TestTraceWriter(System.Diagnostics.TraceLevel.Verbose);
            var scriptHostConfiguration = new ScriptHostConfiguration
            {
                HostConfig = new JobHostConfiguration(),
                TraceWriter = traceWriter,
                FileLoggingEnabled = true,
                FileWatchingEnabled = true
            };

            var host = new Mock<ScriptHost>(scriptHostConfiguration);
            var entrypointResolver = new Mock<IFunctionEntryPointResolver>();

            var compilation = new Mock<ICompilation>();
            compilation.Setup(c => c.GetDiagnostics())
                .Returns(ImmutableArray<Diagnostic>.Empty);

            var compilationService = new Mock<ICompilationService>();
            compilationService.Setup(s => s.SupportedFileTypes)
                .Returns(() => new[] { ".csx" });
            compilationService.Setup(s => s.GetFunctionCompilation(It.IsAny<FunctionMetadata>()))
                .Returns(compilation.Object);

            var compilationServiceFactory = new Mock<ICompilationServiceFactory>();
            compilationServiceFactory.Setup(f => f.CreateService(ScriptType.CSharp, It.IsAny<IFunctionMetadataResolver>()))
            .Returns(compilationService.Object);

            return new RunDependencies
            {
                Host = host,
                EntrypointResolver = entrypointResolver,
                Compilation = compilation,
                CompilationService = compilationService,
                CompilationServiceFactory = compilationServiceFactory
            };
        }

        private class RunDependencies
        {
            public Mock<ScriptHost> Host { get; set; }
            public Mock<IFunctionEntryPointResolver> EntrypointResolver { get; set; }
            public Mock<ICompilation> Compilation { get; set; }
            public Mock<ICompilationService> CompilationService { get; set; }
            public Mock<ICompilationServiceFactory> CompilationServiceFactory { get; set; }
        }
    }
}
