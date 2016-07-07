// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WebJobs.Script.Cli.Common
{
    internal enum Newable
    {
        None = 0,
        Function,
        FunctionApp,
        StorageAccount,
        Secret
    }
}
