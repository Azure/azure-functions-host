// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface IJavaScriptCompilation : ICompilation<string>
    {
        bool SupportsDiagnostics { get; }
    }
}
