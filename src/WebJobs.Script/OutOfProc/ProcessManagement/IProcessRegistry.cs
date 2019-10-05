// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal interface IProcessRegistry
    {
        // Registers processes to ensure that they are cleaned up on host exit.
        bool Register(Process process);
    }
}
