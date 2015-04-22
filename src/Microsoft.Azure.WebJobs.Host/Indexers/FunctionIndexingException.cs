// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal class FunctionIndexingException : Exception
    {
        public FunctionIndexingException(string methodName, Exception innerException)
            : base("Error indexing method '" + methodName + "'", innerException)
        {
        }
    }
}
