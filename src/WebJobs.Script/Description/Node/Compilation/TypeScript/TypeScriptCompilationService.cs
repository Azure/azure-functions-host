// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    public class TypeScriptCompilationService : ICompilationService<IJavaScriptCompilation>
    {
        private readonly TypeScriptCompilationOptions _compilationOptions;

        public TypeScriptCompilationService(TypeScriptCompilationOptions compilationOptions)
        {
            _compilationOptions = compilationOptions;
        }

        public string Language => "TypeScript";

        public IEnumerable<string> SupportedFileTypes => new[] { ".ts" };

        public bool PersistsOutput => true;

        async Task<object> ICompilationService.GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
            => await GetFunctionCompilationAsync(functionMetadata);

        public async Task<IJavaScriptCompilation> GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
        {
            return await TypeScriptCompilation.CompileAsync(functionMetadata.ScriptFile, _compilationOptions);
        }
    }
}
