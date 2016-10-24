// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Information about a function definition. 
    /// </summary>
    public interface IFunctionDefinition
    {
        /// <summary>
        /// Name of this function. This can be passed to other queries to drill in further. 
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Function Id. This is semantically the function name and host name so that it is globally unique. 
        /// </summary>
        FunctionId FunctionId { get; }

        /// <summary>
        /// When this function definition was last modified. 
        /// A UI can use this to know when to alert the user that something is out of date (argument list). 
        /// </summary>
        DateTime LastModified { get; }
    }
}