// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISecretManagerProvider
    {
        /// <summary>
        /// Gets a value indicating whether we're configured to use secrets.
        /// </summary>
        bool SecretsEnabled { get; }

        /// <summary>
        /// Gets or creates the <see cref="ISecretManager"./>
        /// </summary>
        ISecretManager Current { get; }
    }
}
