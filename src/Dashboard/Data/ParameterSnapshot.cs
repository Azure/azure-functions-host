// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Protocols;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    [JsonConverter(typeof(ParameterSnapshotConverter))]
    public abstract class ParameterSnapshot
    {
        /// <summary>Gets or sets the parameter type.</summary>
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
