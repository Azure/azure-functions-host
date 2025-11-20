// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers;

public class WorkerProcessInfo
{
    public int ProcessId { get; set; }

    public string ProcessName { get; set; }

    public string DebugEngine { get; set; }
}
