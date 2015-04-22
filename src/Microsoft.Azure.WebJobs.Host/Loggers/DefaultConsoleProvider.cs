// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class DefaultConsoleProvider : IConsoleProvider
    {
        public TextWriter Out
        {
            get { return Console.Out; }
        }
    }
}
