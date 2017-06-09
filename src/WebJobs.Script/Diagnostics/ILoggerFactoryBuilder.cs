// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides ways to plug into the ScriptHost ILoggerFactory initialization.
    /// </summary>
    public interface ILoggerFactoryBuilder
    {
        /// <summary>
        /// Adds additional <see cref="ILoggerProvider"/>s to the <see cref="ILoggerFactory"/>.
        /// </summary>
        /// <param name="factory">The <see cref="ILoggerFactory"/>.</param>
        /// <param name="scriptConfig">The <see cref="ScriptHostConfiguration"/> used by the current <see cref="ScriptHost"/>.</param>
        /// <param name="settingsManager">The <see cref="ScriptSettingsManager"/> used by the current <see cref="ScriptHost"/>.</param>
        void AddLoggerProviders(ILoggerFactory factory, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager);
    }
}
