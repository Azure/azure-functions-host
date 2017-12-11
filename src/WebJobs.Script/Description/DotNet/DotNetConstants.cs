// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public static class DotNetConstants
    {
        public const string PrivateAssembliesFolderName = "bin";
        public const string ProjectFileName = "project.json";
        public const string ProjectLockFileName = "project.lock.json";

        public const string MissingFunctionEntryPointCompilationCode = "AF001";
        public const string AmbiguousFunctionEntryPointsCompilationCode = "AF002";
        public const string MissingTriggerArgumentCompilationCode = "AF003";
        public const string MissingBindingArgumentCompilationCode = "AF004";
        public const string RedundantPackageAssemblyReference = "AF005";
        public const string InvalidFileMetadataReferenceCode = "AF006";
        public const string InvalidEntryPointNameCompilationCode = "AF007";
        public const string AsyncVoidCode = "AF008";
        public const string InvalidPrivateMetadataReferenceCode = "AF009";
    }
}
