// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    /// <summary>
    /// This <see cref="ParameterSnapshot"/> Type is the single Type used for snapshots of parameters
    /// provided by extension bindings. I.e., extensions don't use the explicit subclass model that our
    /// built in bindings use. Instead, extensions provide a <see cref="ParameterDisplayHints"/> via their
    /// <see cref="ParameterDescriptor"/> and we construct an instance of this class from that.
    /// </summary>
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
            // Extensions don't currently support this value. We might decide to
            // open this up in the future.
            get { return string.Empty; }
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