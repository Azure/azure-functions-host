// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.Analyzers
{
    internal static class Constants
    {
        internal static class AssemblyNames
        {
            public const string WebJobs = "Microsoft.Azure.WebJobs";
            public const string WebJobsHost = "Microsoft.Azure.WebJobs.Host";
        }

        internal static class Types
        {
            public const string FunctionNameAttribute = "Microsoft.Azure.WebJobs.FunctionNameAttribute";
        }

        internal static class DiagnosticsCategories
        {
            public const string Reliability = "Reliability";
            public const string Usage = "Usage";
            public const string WebJobsBindings = "WebJobsBindings";
        }
    }
}
