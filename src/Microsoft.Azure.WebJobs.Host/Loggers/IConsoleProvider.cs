// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal interface IConsoleProvider
    {
        TextWriter Out { get; }
    }
}
