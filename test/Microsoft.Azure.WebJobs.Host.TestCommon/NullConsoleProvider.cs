// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
