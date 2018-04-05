// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public enum ScriptType
    {
        Javascript,
        CSharp,
        FSharp,
        [System.Obsolete("The legacy .NET assembly raw reference model has been removed. Use the direct load model instead. For more information, see https://go.microsoft.com/fwlink/?linkid=871978")]
        DotNetAssembly,
        TypeScript,
        JavaArchive,
        Unknown
    }
}