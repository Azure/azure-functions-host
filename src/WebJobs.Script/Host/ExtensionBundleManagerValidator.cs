// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    internal sealed class ExtensionBundleManagerValidator : IFunctionAppValidator
    {
        private readonly IExtensionBundleManager _extensionBundleManager;

        public ExtensionBundleManagerValidator(IExtensionBundleManager extensionBundleManager)
        {
            _extensionBundleManager = extensionBundleManager;
        }

        public void Validate(ScriptJobHostOptions options, IEnvironment environment, ILogger logger)
        {
            if (!logger.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            string outdatedBundleVersion = _extensionBundleManager.GetOutdatedBundleVersion();
            if (!string.IsNullOrEmpty(outdatedBundleVersion))
            {
                logger.OutdatedExtensionBundle(outdatedBundleVersion);
            }
        }
    }
}
