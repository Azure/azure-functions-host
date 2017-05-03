// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Describe a possible binding. 
    // This is used for diagnostics to display possible bindings. 
    internal class Rule
    {
        public static readonly Rule[] Empty = new Rule[0];
                
        // If non-null, then there is some filter on this rule. 
        public string Filter { get; set; }

        // The source-attribute that this rule applies to. 
        public Type SourceAttribute { get; set; }

        // If set, then there are intermediate conversions to get to the UserType
        public Type[] Converters { get; set; } 

        // The possible user parameter type this can bind to. 
        public OpenType UserType { get; set; }
    }
}
