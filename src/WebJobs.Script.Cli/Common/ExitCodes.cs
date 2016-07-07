// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WebJobs.Script.Cli.Common
{
    internal static class ExitCodes
    {
        public const int Success = 0;
        public const int GeneralError = 1;
        public const int MustRunAsAdmin = 2;
        public const int ParseError = 3;
    }
}
