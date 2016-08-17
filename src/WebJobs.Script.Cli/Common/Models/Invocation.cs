// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace WebJobs.Script.Cli.Common.Models
{
    public class Invocation
    {
        public int Id { get; set; }

        public string Verb { get; set; }

        public string UserVerb { get; set; }

        public DateTime Timestamp { get; set; }

        public InvocationResult Result { get; set; }
    }

    public enum InvocationResult
    {
        Error,
        Success
    }
}
