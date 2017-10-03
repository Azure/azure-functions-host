// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class RawJavaScriptCompilation : IJavaScriptCompilation
    {
        private readonly string _scriptFilePath;

        public RawJavaScriptCompilation(string scriptFilePath)
        {
            _scriptFilePath = scriptFilePath;
        }

        public bool SupportsDiagnostics => false;

        public ImmutableArray<Diagnostic> GetDiagnostics() => ImmutableArray<Diagnostic>.Empty;

        async Task<object> ICompilation.EmitAsync(CancellationToken cancellationToken) => await EmitAsync(cancellationToken);

        public Task<string> EmitAsync(CancellationToken cancellationToken) => Task.FromResult(_scriptFilePath);
    }
}
