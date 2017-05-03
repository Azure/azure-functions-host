// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Attribute used to indicate the name to use for a job function.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class FunctionNameAttribute : Attribute
    {
        private string _name;

        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionNameAttribute"/> class with a given name.
        /// </summary>
        /// <param name="name">Name of the function.</param>
        public FunctionNameAttribute(string name)
        {
            this._name = name;
        }

        /// <summary>
        /// Gets the function name.
        /// </summary>
        public string Name => _name;
    }
}
