// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    /// <summary>
    /// Exception that occurs when the <see cref="JobHost"/> encounters errors when trying
    /// to index job methods on startup.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors")]
    [Serializable]
    public class FunctionIndexingException : Exception
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="methodName">The name of the method in error.</param>
        /// <param name="innerException">The inner exception.</param>
        public FunctionIndexingException(string methodName, Exception innerException)
            : base("Error indexing method '" + methodName + "'", innerException)
        {
            MethodName = methodName;
        }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/>.</param>
        /// <param name="context">The <see cref="StreamingContext"/>.</param>
        protected FunctionIndexingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            MethodName = info.GetString("MethodName");
            Handled = bool.Parse(info.GetString("Handled"));
        }

        /// <summary>
        /// The name of the method in error.
        /// </summary>
        public string MethodName { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether the exception should be treated
        /// as handled.
        /// </summary>
        public bool Handled { get; set; }

        /// <inheritdoc/>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            info.AddValue("MethodName", this.MethodName);
            info.AddValue("Handled", this.Handled);

            base.GetObjectData(info, context);
        }
    }
}
