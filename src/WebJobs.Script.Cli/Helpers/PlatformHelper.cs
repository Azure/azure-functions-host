// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace WebJobs.Script.Cli
{
    internal static class PlatformHelper
    {
        public static bool IsMono { get; }  = Type.GetType("Mono.Runtime") != null;
        public static bool IsWindows { get; } = Environment.OSVersion.Platform == PlatformID.Win32NT;
    }
}
