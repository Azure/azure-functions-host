// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal sealed class ScriptRuntimeAssembly
    {
        public string Name { get; set; }

        public string ResolutionPolicy { get; set; }
    }
}
