// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    public interface ITypeScriptCompiler
    {
        Task<ImmutableArray<Diagnostic>> CompileAsync(string inputFile, TypeScriptCompilationOptions options);
    }
}
