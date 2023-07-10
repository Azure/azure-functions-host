// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class DiagnosticEventConstants
    {
        public const string HostIdCollisionErrorCode = "AZFD0004";
        public const string HostIdCollisionHelpLink = "https://go.microsoft.com/fwlink/?linkid=2224100";

        public const string ExternalStartupErrorCode = "AZFD0005";
        public const string ExternalStartupErrorHelpLink = "https://go.microsoft.com/fwlink/?linkid=2224847";

        public const string SasTokenExpiringErrorCode = "AZFD0006";
        public const string SasTokenExpiringErrorHelpLink = "https://go.microsoft.com/fwlink/?linkid=2244092";

        public const string MaximumSecretBackupCountErrorCode = "AZFD0007";
        public const string MaximumSecretBackupCountHelpLink = "https://go.microsoft.com/fwlink/?linkid=1234569";

        public const string FailedToReadBlobStorageRepositoryErrorCode = "AZFD0007";
        public const string FailedToReadBlobStorageRepositoryHelpLink = "https://go.microsoft.com/fwlink/?linkid=7891011";
    }
}
