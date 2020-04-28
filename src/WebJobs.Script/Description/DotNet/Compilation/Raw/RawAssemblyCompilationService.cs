// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class RawAssemblyCompilationService : ICompilationService<IDotNetCompilation>
    {
        private static string[] _supportedFileTypes = new[] { ".dll", ".exe" };

        public string Language => DotNetScriptTypes.RawDotNetAssembly;

        public IEnumerable<string> SupportedFileTypes => _supportedFileTypes;

        public bool PersistsOutput => false;

        async Task<object> ICompilationService.GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
            => await GetFunctionCompilationAsync(functionMetadata);

        public Task<IDotNetCompilation> GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
        {
            return Task.FromResult<IDotNetCompilation>(new RawAssemblyCompilation(functionMetadata.ScriptFile, functionMetadata.EntryPoint));
        }
    }
}
