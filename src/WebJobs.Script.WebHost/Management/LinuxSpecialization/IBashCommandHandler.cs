// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public interface IBashCommandHandler
    {
        (string Output, string Error, int ExitCode) RunBashCommand(string command, string metricName);
    }
}