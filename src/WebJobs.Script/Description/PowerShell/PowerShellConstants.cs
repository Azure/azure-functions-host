// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description.PowerShell
{
    public static class PowerShellConstants
    {
        // Module path constants
        public const string ModulesFolderName = "modules";
        public const string ModulesScriptFileExtensionPattern = "*psm1";
        public const string ModulesManifestFileExtensionPattern = "*psd1";
        public const string ModulesBinaryFileExtensionPattern = "*dll";

        // Error message constants
        public const int SpaceCount = 4;
        public const char SpaceChar = ' ';
        public const char UnderscoreChar = '_';
        public const char AdditionChar = '+';
        public const string StackTraceScriptBlock = @"<ScriptBlock>, <No file>";

        // Output constants
        public const string CategoryInfoLabel = "CategoryInfo          :";
        public const string FullyQualifiedErrorIdLabel = "FullyQualifiedErrorId :";
    }
}
