// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionExecutionContext
    {
        public string FunctionName { get; set; }

        public string FunctionDirectory { get; set; }

        public Guid InvocationId { get; set; }
    }
}
