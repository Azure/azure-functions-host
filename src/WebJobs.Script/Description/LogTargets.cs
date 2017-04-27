// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Specifies the log to target.
    /// </summary>
    [Flags]
    internal enum LogTargets
    {
        None = 0,
        System = 1,
        User = 2,
        All = System | User
    }
}
