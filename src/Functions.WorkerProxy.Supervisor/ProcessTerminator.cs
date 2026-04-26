// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.Azure.Functions.WorkerProxy.Supervisor;

internal static partial class ProcessTerminator
{
    private const int SigTerm = 15;

    public static void RequestGracefulTermination(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (process.HasExited)
        {
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (Kill(process.Id, SigTerm) == 0)
            {
                return;
            }
        }

        process.Kill(entireProcessTree: true);
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int sig);
}
