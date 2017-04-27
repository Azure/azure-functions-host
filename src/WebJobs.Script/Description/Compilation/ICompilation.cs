// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface ICompilation
    {
        ImmutableArray<Diagnostic> GetDiagnostics();

        object Emit(CancellationToken cancellationToken);
    }

    public interface ICompilation<TOutput> : ICompilation
    {
        new TOutput Emit(CancellationToken cancellationToken);
    }
}
