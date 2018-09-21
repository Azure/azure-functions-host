// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public static class ArmHelpers
    {
        public const string GeoLocationHeaderKey = "x-ms-geo-location";

        public static object AddEnvelopeOnArmRequest<T>(T namedObject, HttpRequest request) where T : INamedObject
        {
            if (IsArmRequest(request))
            {
                return Create(namedObject, request);
            }

            return namedObject;
        }

        public static object AddEnvelopeOnArmRequest<T>(List<T> namedObjects, HttpRequest request) where T : INamedObject
        {
            return AddEnvelopeOnArmRequest((IEnumerable<T>)namedObjects, request);
        }

        public static object AddEnvelopeOnArmRequest<T>(IEnumerable<T> namedObjects, HttpRequest request) where T : INamedObject
        {
            if (IsArmRequest(request))
            {
                return Create(namedObjects, request);
            }

            return namedObjects;
        }

        public static bool IsArmRequest(HttpRequest request)
        {
            var headers = request?.GetTypedHeaders();
            return request != null &&
                   request.Headers != null &&
                   request.Headers.ContainsKey(GeoLocationHeaderKey) &&
                   headers.Referer.AbsolutePath.IndexOf("hostruntime", StringComparison.OrdinalIgnoreCase) == -1;
        }

        private static ArmListEntry<T> Create<T>(IEnumerable<T> objects, HttpRequest request) where T : INamedObject
        {
            return new ArmListEntry<T>
            {
                Value = objects.Select(entry => Create(entry, request, isChild: true))
            };
        }

        private static ArmEntry<T> Create<T>(T o, HttpRequest request, bool isChild = false) where T : INamedObject
        {
            var armEntry = new ArmEntry<T>()
            {
                Properties = o
            };

            // In Azure ARM requests, the referrer is the current id
            Uri referrer = request.GetTypedHeaders().Referer;
            armEntry.Id = referrer != null ? referrer.AbsolutePath : request.Path.ToString();

            // If we're generating a child object, append the child name
            if (isChild)
            {
                if (!armEntry.Id.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                {
                    armEntry.Id += '/';
                }

                armEntry.Id += ((INamedObject)o).Name;
            }

            armEntry.Id = armEntry.Id.TrimEnd('/');

            // The Type and Name properties use alternating token starting with 'Microsoft.Web/sites'
            // e.g. /subscriptions/b0019e1d-2829-4226-9356-4a57a4a5cc90/resourcegroups/MyRG/providers/Microsoft.Web/sites/MySite/extensions/SettingsAPISample/settings/foo1
            // Type: Microsoft.Web/sites/extensions/settings
            // Name: MySite/SettingsAPISample/foo1

            string[] idTokens = armEntry.Id.Split('/');
            if (idTokens.Length > 8 && idTokens[6] == "Microsoft.Web")
            {
                armEntry.Type = idTokens[6];

                for (int i = 7; i < idTokens.Length; i += 2)
                {
                    armEntry.Type += "/" + idTokens[i];
                }

                armEntry.Name = idTokens[8];
                for (int i = 10; i < idTokens.Length; i += 2)
                {
                    armEntry.Name += "/" + idTokens[i];
                }
            }

            if (request.Headers.TryGetValue(GeoLocationHeaderKey, out StringValues values))
            {
                armEntry.Location = values.FirstOrDefault();
            }

            return armEntry;
        }

        public static ArmErrorInfo CreateErrorResponse(int statusCode, string errorMessage)
        {
            return new ArmErrorInfo(statusCode, errorMessage);
        }

        // this error will be deserialized conforming with ARM spec
        public class ArmErrorInfo
        {
            public ArmErrorInfo(int statusCode, string errorMessage)
            {
                Error = new ArmErrorDetails
                {
                    Code = statusCode.ToString(),
                    Message = errorMessage
                };
            }

            [JsonProperty(PropertyName = "error")]
            public ArmErrorDetails Error { get; private set; }

            public class ArmErrorDetails
            {
                [JsonProperty(PropertyName = "code")]
                public string Code { get; set; }

                [JsonProperty(PropertyName = "message")]
                public string Message { get; set; }
            }
        }
    }
}
