// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WarmUpConstants
    {
        public const string FunctionName = "WarmUp";
        public const string AlternateRoute = "CSharpHttpWarmup";
        public const string PreJitFolderName = "PreJIT";
        public const string JitTraceFileName = "coldstart.jittrace";
        public const string LinuxJitTraceFileName = "linux.coldstart.jittrace";
    }
}
