// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Config
{
    /// <summary>
    /// Defines an interface enabling 3rd party extensions to participate in the <see cref="JobHost"/> configuration
    /// process to register their own extension types. Any registered <see cref="IExtensionConfigProvider"/> instances
    /// added to the service container will be invoked at the right time during startup.
    /// </summary>
    public interface IExtensionConfigProvider
    {
        /// <summary>
        /// Initializes the extension.
        /// </summary>
        void Initialize();
    }
}
