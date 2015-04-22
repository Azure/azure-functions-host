// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Host.Loggers;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class NullConsoleProvider : IConsoleProvider
    {
        public TextWriter Out
        {
            get { return TextWriter.Null; }
        }
    }
}
