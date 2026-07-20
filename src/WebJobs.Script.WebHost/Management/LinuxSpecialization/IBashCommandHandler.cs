// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public interface IBashCommandHandler
    {
        (string Output, string Error, int ExitCode) RunCommand(string fileName, IReadOnlyList<string> arguments, string metricName);
    }
}