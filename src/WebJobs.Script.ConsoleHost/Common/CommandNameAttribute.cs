// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace WebJobs.Script.ConsoleHost.Common
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CommandNamesAttribute : Attribute
    {
        public string[] Names { get; }

        public CommandNamesAttribute(params string[] names)
        {
            Names = names;
        }
    }
}