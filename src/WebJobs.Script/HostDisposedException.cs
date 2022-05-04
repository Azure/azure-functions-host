// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using Microsoft.Azure.WebJobs.Host.Listeners;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// An exception that indicates that a service was used on a disposed host.
    /// </summary>
    [Serializable]
    public class HostDisposedException : ObjectDisposedException
    {
        // For this exception, we want a full stack trace to pinpoint the method
        // that is trying to use a disposed host.
        private readonly string _stackTraceString;

        public HostDisposedException(string disposedObjectName, Exception inner)
            : base(GetDefaultMessage(disposedObjectName), inner)
        {
            _stackTraceString = GetStackTraceString();
        }

        protected HostDisposedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            _stackTraceString = info.GetString("FullStackTraceString");
        }

        public override string StackTrace => _stackTraceString;

        public string GetStackTraceString()
        {
            return new StackTrace(true).ToString();
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);

            info.AddValue("FullStackTraceString", _stackTraceString);
        }

        private static string GetDefaultMessage(string disposedObjectName)
        {
            string message = $"The host is disposed and cannot be used. Disposed object: '{disposedObjectName}'";

            var stack = new StackTrace(true);

            foreach (StackFrame frame in stack.GetFrames())
            {
                Type declaringType = frame.GetMethod()?.DeclaringType;

                if (typeof(IListener).IsAssignableFrom(declaringType))
                {
                    message += $"; Found IListener in stack trace: '{declaringType.AssemblyQualifiedName}'";
                }
            }

            return message;
        }
    }
}
