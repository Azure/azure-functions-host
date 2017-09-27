// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public interface IRpcServer
    {
        void Start();

        Task ShutdownAsync();

        Uri Uri { get; }
    }
}
