// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ConditionalJavaScriptCompilationService : ICompilationService<IJavaScriptCompilation>
    {
        private readonly ICompilationService<IJavaScriptCompilation> _compilationService;
        private readonly Func<bool> _predicate;
        private readonly ScriptSettingsManager _settingsManager;

        public ConditionalJavaScriptCompilationService(ScriptSettingsManager settingsManager, ICompilationService<IJavaScriptCompilation> compilationService, Func<bool> predicate)
        {
            _settingsManager = settingsManager;
            _compilationService = compilationService;
            _predicate = predicate;
        }

        public string Language => _compilationService.Language;

        public IEnumerable<string> SupportedFileTypes => _compilationService.SupportedFileTypes;

        public bool PersistsOutput => _compilationService.PersistsOutput;

        public async Task<IJavaScriptCompilation> GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
        {
            if (_predicate())
            {
                try
                {
                    IJavaScriptCompilation compilation = await _compilationService.GetFunctionCompilationAsync(functionMetadata);

                    await PersistCompilationResult(functionMetadata, compilation.Emit(CancellationToken.None), compilation.GetDiagnostics());

                    return compilation;
                }
                catch (CompilationErrorException exc)
                {
                    await PersistCompilationResult(functionMetadata, null, exc.Diagnostics);
                    throw;
                }
            }

            return await GetCompilationResultAsync(functionMetadata);
        }

        async Task<object> ICompilationService.GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
            => await GetFunctionCompilationAsync(functionMetadata);

        private async Task PersistCompilationResult(FunctionMetadata metadata, string emitResult, ImmutableArray<Diagnostic> immutableArray)
        {
            var state = (emitResult != null && !immutableArray.Any(d => d.Severity == DiagnosticSeverity.Error))
                ? JavaScriptCompilation.CompilationState.Succeeded
                : JavaScriptCompilation.CompilationState.Failed;

            var compilationResult = new JavaScriptCompilation()
            {
                State = state,
                EmitResult = emitResult,
                Diagnostics = new List<Diagnostic>(immutableArray)
            };

            string sentinelFilePath = GetSentinelFilePath(metadata.Name);
            FileUtility.EnsureDirectoryExists(Path.GetDirectoryName(sentinelFilePath));
            await FileUtility.WriteAsync(sentinelFilePath, JsonConvert.SerializeObject(compilationResult));
        }

        private async Task<IJavaScriptCompilation> GetCompilationResultAsync(FunctionMetadata functionMetadata)
        {
            DateTime compilationStarted = DateTime.UtcNow;
            string sentinelFilePath = GetSentinelFilePath(functionMetadata.Name);
            int iterationInterval = 500;
            int maxDuration = 30000;
            int iterationCount = maxDuration / iterationInterval;
            for (int i = 0; i < iterationCount; i++)
            {
                if (File.Exists(sentinelFilePath) && File.GetLastWriteTimeUtc(sentinelFilePath) > compilationStarted)
                {
                    string json = await FileUtility.ReadAsync(sentinelFilePath);

                    try
                    {
                        JavaScriptCompilation result = JsonConvert.DeserializeObject<JavaScriptCompilation>(json);

                        if (result.State == JavaScriptCompilation.CompilationState.Failed)
                        {
                            throw new CompilationErrorException("Compilation failed", result.Diagnostics.ToImmutableArray());
                        }
                        else if (result.State == JavaScriptCompilation.CompilationState.Succeeded)
                        {
                            return result;
                        }
                    }
                    catch (JsonSerializationException)
                    {
                        // Continue and wait on timeout
                    }
                }

                await Task.Delay(500);
            }

            throw new CompilationErrorException("Compilation timed out.", ImmutableArray<Diagnostic>.Empty);
        }

        private string GetSentinelFilePath(string functionName)
        {
            string home = null;
            if (_settingsManager.IsAppServiceEnvironment)
            {
                home = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
            }
            else
            {
                home = Path.Combine(Path.GetTempPath(), "AzureFunctions");
            }

            return Path.Combine(home, $@"data\Functions\CompilationOutput\{Language}\{functionName}\.output");
        }

        private class JavaScriptCompilation : IJavaScriptCompilation
        {
            public enum CompilationState
            {
                Running,
                Succeeded,
                Failed
            }

            public CompilationState State { get; set; }

            public string EmitResult { get; set; }

            public List<Diagnostic> Diagnostics { get; set; }

            [JsonIgnore]
            public bool SupportsDiagnostics => true;

            public ImmutableArray<Diagnostic> GetDiagnostics() => Diagnostics?.ToImmutableArray() ?? ImmutableArray<Diagnostic>.Empty;

            public string Emit(CancellationToken cancellationToken) => EmitResult;

            object ICompilation.Emit(CancellationToken cancellationToken) => Emit(cancellationToken);
        }
    }
}
