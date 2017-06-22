// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class PlatformHelper
    {
        public static bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}
