// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface ICompilation
    {
        ImmutableArray<Diagnostic> GetDiagnostics();

        Task<object> EmitAsync(CancellationToken cancellationToken);
    }

    public interface ICompilation<TOutput> : ICompilation
    {
        new Task<TOutput> EmitAsync(CancellationToken cancellationToken);
    }
}
