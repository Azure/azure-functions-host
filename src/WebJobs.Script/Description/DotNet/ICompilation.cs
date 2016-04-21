// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [CLSCompliant(false)]
    public interface ICompilation
    {
        ImmutableArray<Diagnostic> GetDiagnostics();

        FunctionSignature GetEntryPointSignature(IFunctionEntryPointResolver entryPointResolver);

        void Emit(Stream assemblyStream, Stream pdbStream, CancellationToken cancellationToken);
    }
}
