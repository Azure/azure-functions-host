// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("Invoke")]
    internal class InvokeParameterSnapshot : ParameterSnapshot
    {
        public override string Description
        {
            get { return "Caller-supplied value"; }
        }

        public override string AttributeText
        {
            get { return null; }
        }

        public override string Prompt
        {
            get { return "Enter a value"; }
        }

        public override string DefaultValue
        {
            get { return null; }
        }
    }
}
