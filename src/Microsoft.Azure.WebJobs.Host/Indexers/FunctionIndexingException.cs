// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    [SuppressMessage("Microsoft.Design", "CA1064:ExceptionsShouldBePublic")]
    [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors")]
    [Serializable]
    internal class FunctionIndexingException : Exception
    {
        public FunctionIndexingException(string methodName, Exception innerException)
            : base("Error indexing method '" + methodName + "'", innerException)
        {
        }
    }
}
