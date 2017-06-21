// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [Serializable]
    public sealed class CompilationServiceException : Exception
    {
        public CompilationServiceException()
        {
        }

        public CompilationServiceException(string message) : base(message)
        {
        }

        public CompilationServiceException(string message, Exception innerException) : base(message, innerException)
        {
        }

        private CompilationServiceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
