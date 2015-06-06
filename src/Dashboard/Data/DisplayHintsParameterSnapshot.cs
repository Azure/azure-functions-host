// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("DisplayHints")]
    public class DisplayHintsParameterSnapshot : ParameterSnapshot
    {
        public DisplayHintsParameterSnapshot()
        {
            DisplayHints = new ParameterDisplayHints();
        }

        public DisplayHintsParameterSnapshot(ParameterDisplayHints displayHints)
        {
            DisplayHints = displayHints;
        }

        public ParameterDisplayHints DisplayHints { get; set; }

        public override string Description
        {
            get { return DisplayHints.Description; }
        }

        public override string AttributeText
        {
            get { return DisplayHints.AttributeText; }
        }

        public override string Prompt
        {
            get { return DisplayHints.Prompt; }
        }

        public override string DefaultValue
        {
            get { return DisplayHints.DefaultValue; }
        }
    }
}