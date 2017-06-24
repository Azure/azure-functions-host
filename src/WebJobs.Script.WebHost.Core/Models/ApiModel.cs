// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// A wrapper that enriches objects exposed by the REST API to provide a consistent model.
    /// </summary>
    public class ApiModel : JObject
    {
        public ApiModel()
        {
        }

        public Collection<Link> Links { get; set; }

        public override void WriteTo(JsonWriter writer, params JsonConverter[] converters)
        {
            writer.WriteStartObject();

            for (int i = 0; i < ChildrenTokens.Count; i++)
            {
                ChildrenTokens[i].WriteTo(writer, converters);
            }

            if (this.Links != null)
            {
                writer.WritePropertyName("links");
                JToken.FromObject(this.Links).WriteTo(writer, converters);
            }

            writer.WriteEndObject();
        }
    }
}
