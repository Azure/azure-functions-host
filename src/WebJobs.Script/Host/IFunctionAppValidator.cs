// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    /// <summary>
    /// Defines a validator interface for Function App validation.
    /// </summary>
    internal interface IFunctionAppValidator
    {
        /// <summary>
        /// Validates aspects of the function app and reports any issues through the provided logger.
        /// </summary>
        /// <param name="options">The script host options containing function app configuration.</param>
        /// <param name="environment">The environment in which the function app is running.</param>
        /// <param name="logger">The logger to report validation issues.</param>
        void Validate(ScriptJobHostOptions options, IEnvironment environment, ILogger logger);
    }
}
