// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface ICompilationService
    {
        string Language { get; }

        IEnumerable<string> SupportedFileTypes { get; }

        ICompilation GetFunctionCompilation(FunctionMetadata functionMetadata);
    }
}
