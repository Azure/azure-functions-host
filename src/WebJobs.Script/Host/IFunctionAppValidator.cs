// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    /// <summary>
    /// A validator interface for function app payload validation.
    /// </summary>
    internal interface IFunctionAppValidator
    {
        void Validate(ScriptJobHostOptions options, IEnvironment environment, ILogger logger);
    }
}
