// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.WebJobs.Protocols;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    [JsonConverter(typeof(ParameterSnapshotConverter))]
    public abstract class ParameterSnapshot
    {
        /// <summary>Gets or sets the parameter type.</summary>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public string Type { get; set; }

        private class ParameterSnapshotConverter : PolymorphicJsonConverter
        {
            public ParameterSnapshotConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<ParameterSnapshot>())
            {
            }
        }

        [JsonIgnore]
        public abstract string Description { get; }

        [JsonIgnore]
        public abstract string AttributeText { get; }

        [JsonIgnore]
        public abstract string Prompt { get; }

        [JsonIgnore]
        public abstract string DefaultValue { get; }
    }
}
