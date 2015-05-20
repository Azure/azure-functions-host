// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Config
{
    /// <summary>
    /// Context object passed to <see cref="IExtensionConfigProvider"/> instances when
    /// they are initialized.
    /// </summary>
    public class ExtensionConfigContext
    {
        /// <summary>
        /// Gets or sets the <see cref="JobHostConfiguration"/>
        /// </summary>
        public JobHostConfiguration Config { get; set; }
    }
}
