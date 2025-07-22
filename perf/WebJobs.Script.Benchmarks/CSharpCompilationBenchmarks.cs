// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using BenchmarkDotNet.Attributes;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class CSharpCompilationBenchmarks
    {
        // Set of samples to benchmark
        // TODOO: BlobTrigger, needs assembly refs working
        [Params("DocumentDB", "HttpTrigger", "HttpTrigger-Cancellation", "HttpTrigger-CustomRoute", "NotificationHub")]
        public string BenchmarkTrigger;

        // Script source
        private string ScriptPath;
        private static string GetCSharpSamplePath([CallerFilePath] string thisFilePath = null) =>
            Path.Combine(thisFilePath, "..", "..", "..", "sample", "CSharp");
        private string ScriptSource;
        private FunctionMetadata FunctionMetadata;

        // Dyanmic Compilation
        private readonly InteractiveAssemblyLoader AssemblyLoader = new InteractiveAssemblyLoader();
        private IFunctionMetadataResolver Resolver;
        private CSharpCompilationService CompilationService;

        private IDotNetCompilation ScriptCompilation;
        private DotNetCompilationResult ScriptAssembly;

        [GlobalSetup]
        public async Task SetupAsync()
        {
            ScriptPath = Path.Combine(GetCSharpSamplePath(), BenchmarkTrigger, "run.csx");
            ScriptSource = File.ReadAllText(ScriptPath);
            FunctionMetadata = new FunctionMetadata()
            {
                FunctionDirectory = Path.GetDirectoryName(ScriptPath),
                ScriptFile = ScriptPath,
                Name = BenchmarkTrigger,
                Language = DotNetScriptTypes.CSharp
            };

            Resolver = new ScriptFunctionMetadataResolver(ScriptPath, Array.Empty<IScriptBindingProvider>(), NullLogger.Instance);
            CompilationService = new CSharpCompilationService(Resolver, OptimizationLevel.Release);

            ScriptCompilation = await CompilationService.GetFunctionCompilationAsync(FunctionMetadata);
            ScriptAssembly = await ScriptCompilation.EmitAsync(default);
        }

        [Benchmark(Description = nameof(CSharpScript) + "." + nameof(CSharpScript.Create))]
        public Script<object> ScriptCreation() => 
            CSharpScript.Create(ScriptSource, options: Resolver.CreateScriptOptions(), assemblyLoader: AssemblyLoader);

        [Benchmark(Description = nameof(CSharpCompilationService) + "." + nameof(CSharpCompilationService.GetFunctionCompilationAsync))]
        public Task<IDotNetCompilation> GetFunctionCompilationAsync() => CompilationService.GetFunctionCompilationAsync(FunctionMetadata);

        [Benchmark(Description = nameof(CSharpCompilationBenchmarks) + "." + nameof(CSharpCompilationBenchmarks.EmitAsync))]
        public Task<DotNetCompilationResult> EmitAsync() => ScriptCompilation.EmitAsync(default);

        [Benchmark(Description = nameof(DotNetCompilationResult) + "." + nameof(DotNetCompilationResult.Load))]
        public void Load() => ScriptAssembly.Load(FunctionMetadata,Resolver, NullLogger.Instance);
    }
}
