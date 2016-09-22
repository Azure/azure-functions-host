// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace WebJobs.Script.Cli.Extensions
{
    internal static class FunctionStatusExtensions
    {
        public static bool IsHttpFunction(this FunctionStatus functionStatus)
        {
            return functionStatus
                ?.Metadata
                ?.InputBindings
                .Any(i => i.IsTrigger && i.Type.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase)) ?? false;
        }
    }
}
