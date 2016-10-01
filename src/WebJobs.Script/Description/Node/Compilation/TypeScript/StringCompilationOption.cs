// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    public class StringCompilationOption : CompilationOption
    {
        public StringCompilationOption(string name, string value)
            : base(name)
        {
            Value = value;
        }

        public string Value { get; set; }

        public override string ToArgumentString() => $"--{Name} {Value}";
    }
}
