// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides an environment abstraction to access environment variables, properties and configuration.
    /// </summary>
    public interface IEnvironment
    {
        /// <summary>
        /// Gets a value indicating whether the current process is a 64-bit process.
        /// </summary>
        public bool Is64BitProcess { get; }

        /// <summary>
        /// Returns the value of an environment variable for the current <see cref="IEnvironment"/>.
        /// </summary>
        /// <param name="name">The environment variable name.</param>
        /// <returns>The value of the environment variable specified by <paramref name="name"/>, or <see cref="null"/> if the environment variable is not found.</returns>
        string GetEnvironmentVariable(string name);

        /// <summary>
        /// Creates, modifies, or deletes an environment variable stored in the current <see cref="IEnvironment"/>.
        /// </summary>
        /// <param name="name">The environment variable name.</param>
        /// <param name="value">The value to assign to the variable.</param>
        void SetEnvironmentVariable(string name, string value);
    }
}
