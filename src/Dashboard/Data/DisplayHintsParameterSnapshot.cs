// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    public class DisplayHintsParameterSnapshot : ParameterSnapshot
    {
        private ParameterDisplayHints _uiDescriptor;

        public DisplayHintsParameterSnapshot(ParameterDisplayHints uiDescriptor)
        {
            _uiDescriptor = uiDescriptor;
        }

        public override string Description
        {
            get { return _uiDescriptor.Description; }
        }

        public override string AttributeText
        {
            get { return _uiDescriptor.AttributeText; }
        }

        public override string Prompt
        {
            get { return _uiDescriptor.Prompt; }
        }

        public override string DefaultValue
        {
            get { return _uiDescriptor.DefaultValue; }
        }
    }
}