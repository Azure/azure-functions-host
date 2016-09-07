// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// A utility class that enables creation of <see cref="ApiModel"/> and <see cref="Link"/>
    /// instances suitable for consumption over our REST API.
    /// </summary>
    public static class ApiModelUtility
    {
        private static readonly Lazy<JsonSerializer> JsonSerializer;

        static ApiModelUtility()
        {
            JsonSerializer = new Lazy<JsonSerializer>(() =>
            {
                var serializer = new JsonSerializer
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                };

                serializer.Converters.Add(new StringEnumConverter());

                return serializer;
            });
        }

        public static Link CreateLink(HttpRequestMessage request, Uri resourceUri, string relation)
        {
            if (resourceUri == null)
            {
                throw new ArgumentNullException(nameof(resourceUri));
            }

            if (relation == null)
            {
                throw new ArgumentNullException(nameof(relation));
            }

            return new Link
            {
                Relation = relation,
                Href = resourceUri.IsAbsoluteUri ? resourceUri : new Uri($"{GetBaseUri(request)}/{resourceUri}")
            };
        }

        public static ApiModel CreateApiModel(object model, HttpRequestMessage request, string relativeResourcePath = "", bool addSelfLink = true)
        {
            var apiModel = new ApiModel();

            JObject modelJson = JObject.FromObject(model, JsonSerializer.Value);
            apiModel.Merge(modelJson);

            apiModel.Links = new Collection<Link>();
            if (addSelfLink)
            {
                Link link = CreateLink(request, new Uri(GetBaseUri(request, relativeResourcePath)), "self");
                apiModel.Links.Add(link);
            }

            return apiModel;
        }

        internal static string GetBaseUri(HttpRequestMessage request, string suffix = "")
        {
            if (!string.IsNullOrEmpty(suffix) && suffix[0] != '/')
            {
                suffix = "/" + suffix;
            }
            return request.RequestUri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped);
        }
    }
}
