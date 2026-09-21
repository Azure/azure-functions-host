// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class EmptyProcessRegistry : IProcessRegistry
    {
        public bool Register(WorkerProcess process) => true;

        public void Close()
        {
        }
    }
}
