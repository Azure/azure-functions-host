﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public interface IInstanceManager
    {
        IDictionary<string, string> GetInstanceInfo();

        bool StartAssignment(HostAssignmentContext assignmentContext);
    }
}
