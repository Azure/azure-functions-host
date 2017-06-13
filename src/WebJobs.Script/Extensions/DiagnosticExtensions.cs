// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal static class DiagnosticExtensions
    {
        public static object[] GetDiagnosticMessageArguments(this Diagnostic diagnostic)
        {
            return typeof(Diagnostic)
                .GetProperty("Arguments", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(diagnostic) as object[];
        }
    }
}
