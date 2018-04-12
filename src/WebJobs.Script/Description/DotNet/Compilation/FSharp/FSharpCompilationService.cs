// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.FSharp.Compiler;
using Microsoft.FSharp.Compiler.SimpleSourceCodeServices;
using Microsoft.FSharp.Compiler.SourceCodeServices;
using Microsoft.FSharp.Core;
using static Microsoft.Azure.WebJobs.Script.FileUtility;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class FSharpCompilationService : ICompilationService<IDotNetCompilation>
    {
        private static readonly string[] FileTypes = { ".fs", ".fsx", ".dll", ".exe", ".fsi" };
        private readonly IFunctionMetadataResolver _metadataResolver;
        private static readonly Lazy<InteractiveAssemblyLoader> AssemblyLoader
        = new Lazy<InteractiveAssemblyLoader>(() => new InteractiveAssemblyLoader(), LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly OptimizationLevel _optimizationLevel;
        private readonly Regex _hashRRegex;
        private readonly ILogger _logger;

        public FSharpCompilationService(IFunctionMetadataResolver metadataResolver, OptimizationLevel optimizationLevel, ILoggerFactory loggerFactory)
        {
            _metadataResolver = metadataResolver;
            _optimizationLevel = optimizationLevel;
            _hashRRegex = new Regex(@"^\s*#r\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
        }

        public string Language => "FSharp";

        public IEnumerable<string> SupportedFileTypes => FileTypes;

        public bool PersistsOutput => false;

        async Task<object> ICompilationService.GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
            => await GetFunctionCompilationAsync(functionMetadata);

        public Task<IDotNetCompilation> GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
        {
            // First use the C# compiler to resolve references, to get consistency with the C# Azure Functions programming model
            // Add the #r statements from the .fsx file to the resolver source
            string scriptSource = GetFunctionSource(functionMetadata);
            var resolverSourceBuilder = new StringBuilder();

            using (StringReader sr = new StringReader(scriptSource))
            {
                string line;

                while ((line = sr.ReadLine()) != null)
                {
                    if (_hashRRegex.IsMatch(line))
                    {
                        resolverSourceBuilder.AppendLine(line);
                    }
                }
            }

            resolverSourceBuilder.AppendLine("using System;");
            var resolverSource = resolverSourceBuilder.ToString();

            Script<object> script = CodeAnalysis.CSharp.Scripting.CSharpScript.Create(resolverSource, options: _metadataResolver.CreateScriptOptions(), assemblyLoader: AssemblyLoader.Value);

            var compiler = new SimpleSourceCodeServices(msbuildEnabled: FSharpOption<bool>.Some(false));

            FSharpErrorInfo[] errors = null;
            byte[] assemblyBytes = null;
            byte[] pdbBytes = null;

            string scriptPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            Directory.CreateDirectory(scriptPath);

            string scriptFilePath = Path.Combine(scriptPath, Path.GetFileName(functionMetadata.ScriptFile));

            var assemblyName = Utility.GetAssemblyNameFromMetadata(functionMetadata, Guid.NewGuid().ToString());
            var assemblyFileName = Path.Combine(scriptPath, assemblyName + ".dll");
            var pdbName = Path.ChangeExtension(assemblyFileName, PlatformHelper.IsWindows ? "pdb" : "dll.mdb");

            try
            {
                var scriptFileBuilder = new StringBuilder();

                // Write an adjusted version of the script file, prefixing some 'open' declarations
                foreach (string import in script.Options.Imports)
                {
                    scriptFileBuilder.AppendLine("open " + import);
                }

                // Suppress undesirable warnings
                scriptFileBuilder.AppendLine("#nowarn \"988\"");

                // Set the line to match the original script
                scriptFileBuilder.AppendLine("# 0 @\"" + functionMetadata.ScriptFile + "\"");

                // Add our original script
                scriptFileBuilder.AppendLine(scriptSource);

                File.WriteAllText(scriptFilePath, scriptFileBuilder.ToString());

                var otherFlags = new List<string>();

                otherFlags.Add("fsc.exe");

                // The --noframework option is used because we will shortly add references to mscorlib and FSharp.Core
                // as dictated by the C# reference resolver, and F# doesn't like getting multiple references to those.
                otherFlags.Add("--noframework");

                var references = script.GetCompilation().References
                    .Where(m => !(m is UnresolvedMetadataReference))
                    .Select(m => "-r:" + m.Display)
                    .Distinct(new FileNameEqualityComparer());

                // Add the references as reported by the metadata resolver.
                otherFlags.AddRange(references);

                if (_optimizationLevel == OptimizationLevel.Debug)
                {
                    otherFlags.Add("--optimize-");
                    otherFlags.Add("--debug+");
                    otherFlags.Add("--tailcalls-");
                }

                // TODO: FACAVAL verify if this still applies to core
                if (!PlatformHelper.IsWindows)
                {
                    var monoDir = Path.GetDirectoryName(typeof(string).Assembly.Location);
                    var facadesDir = Path.Combine(monoDir, "Facades");
                    otherFlags.Add("--lib:" + facadesDir);
                }

                // If we have a private assembly folder, make sure the compiler uses it to resolve dependencies
                string privateAssembliesFolder = Path.Combine(Path.GetDirectoryName(functionMetadata.ScriptFile), DotNetConstants.PrivateAssembliesFolderName);
                if (Directory.Exists(privateAssembliesFolder))
                {
                    otherFlags.Add("--lib:" + Path.Combine(Path.GetDirectoryName(functionMetadata.ScriptFile), DotNetConstants.PrivateAssembliesFolderName));
                }

                otherFlags.Add("--out:" + assemblyFileName);

                // Get the #load closure
                FSharpChecker checker = FSharpChecker.Create(null, null, null, msbuildEnabled: FSharpOption<bool>.Some(false));
                var loadFileOptionsAsync = checker.GetProjectOptionsFromScript(
                    filename: functionMetadata.ScriptFile,
                    source: scriptSource,
                    loadedTimeStamp: null,
                    otherFlags: null,
                    useFsiAuxLib: null,
                    assumeDotNetFramework: null,
                    extraProjectInfo: null);

                var loadFileOptions = FSharp.Control.FSharpAsync.RunSynchronously(loadFileOptionsAsync, null, null);
                foreach (var loadedFileName in loadFileOptions.Item1.ProjectFileNames)
                {
                    if (Path.GetFileName(loadedFileName) != Path.GetFileName(functionMetadata.ScriptFile))
                    {
                        otherFlags.Add(loadedFileName);
                    }
                }

                // Add the (adjusted) script file itself
                otherFlags.Add(scriptFilePath);

                // Compile the script to a static assembly
                var result = compiler.Compile(otherFlags.ToArray());
                errors = result.Item1;
                var code = result.Item2;

                if (code == 0)
                {
                    assemblyBytes = File.ReadAllBytes(assemblyFileName);
                    pdbBytes = null;
                    if (File.Exists(pdbName))
                    {
                        pdbBytes = File.ReadAllBytes(pdbName);
                    }
                }
                else
                {
                    string message = $"F# compilation failed with arguments: {string.Join(" ", otherFlags)}";
                    _logger.LogDebug(message);
                }
            }
            finally
            {
                DeleteDirectoryAsync(scriptPath, recursive: true)
                .ContinueWith(
                    t => t.Exception.Handle(e =>
                {
                    string message = $"Unable to delete F# compilation file: {e.ToString()}";
                    _logger.LogWarning(message);
                    return true;
                }), TaskContinuationOptions.OnlyOnFaulted);
            }

            return Task.FromResult<IDotNetCompilation>(new FSharpCompilation(errors, assemblyBytes, pdbBytes));
        }

        private static string GetFunctionSource(FunctionMetadata functionMetadata)
        {
            string code = null;

            if (File.Exists(functionMetadata.ScriptFile))
            {
                code = File.ReadAllText(functionMetadata.ScriptFile);
            }

            return code ?? string.Empty;
        }

        private class FileNameEqualityComparer : IEqualityComparer<string>
        {
            public bool Equals(string x, string y)
            {
                if (string.Equals(x, y))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y))
                {
                    return false;
                }

                return string.Equals(Path.GetFileName(x), Path.GetFileName(y));
            }

            public int GetHashCode(string obj)
            {
                if (string.IsNullOrEmpty(obj))
                {
                    return 0;
                }

                return Path.GetFileName(obj).GetHashCode();
            }
        }
    }
}
