// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class RawJavaScriptCompilationService : ICompilationService<IJavaScriptCompilation>
    {
        private readonly Task<IJavaScriptCompilation> _compilationResult;

        public RawJavaScriptCompilationService(FunctionMetadata metadata)
        {
            _compilationResult = Task.FromResult<IJavaScriptCompilation>(new RawJavaScriptCompilation(metadata.ScriptFile));
        }

        public string Language => "JavaScript";

        public bool PersistsOutput => false;

        public IEnumerable<string> SupportedFileTypes => new[] { ".js" };

        async Task<object> ICompilationService.GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
            => await GetFunctionCompilationAsync(functionMetadata);

        public Task<IJavaScriptCompilation> GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
        {
            return _compilationResult;
        }
    }
}
