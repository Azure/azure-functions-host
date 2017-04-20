// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class RawAssemblyCompilationService : ICompilationService
    {
        private static string[] _supportedFileTypes = new[] { ".dll", ".exe" };

        public string Language => "RawDotNetAssembly";

        public IEnumerable<string> SupportedFileTypes => _supportedFileTypes;

        public ICompilation GetFunctionCompilation(FunctionMetadata functionMetadata)
        {
            return new RawAssemblyCompilation(functionMetadata.ScriptFile, functionMetadata.EntryPoint);
        }
    }
}
