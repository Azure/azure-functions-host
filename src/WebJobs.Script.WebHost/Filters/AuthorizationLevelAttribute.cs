// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    public sealed class AuthorizationLevelAttribute : AuthorizationFilterAttribute
    {
        public const string FunctionsKeyHeaderName = "x-functions-key";

        public AuthorizationLevelAttribute(AuthorizationLevel level)
        {
            Level = level;
        }

        public AuthorizationLevel Level { get; private set; }

        public override void OnAuthorization(HttpActionContext actionContext)
        {
            if (actionContext == null)
            {
                throw new ArgumentNullException("actionContext");
            }

            SecretManager secretManager = actionContext.ControllerContext.Configuration.DependencyResolver.GetService<SecretManager>();

            if (!IsAuthorized(actionContext.Request, Level, secretManager))
            {
                actionContext.Response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
        }

        public static bool IsAuthorized(HttpRequestMessage request, AuthorizationLevel level, SecretManager secretManager, string functionName = null)
        {
            if (level == AuthorizationLevel.Anonymous)
            {
                return true;
            }

            AuthorizationLevel requestLevel = GetAuthorizationLevel(request, secretManager, functionName);
            return requestLevel >= level;
        }

        internal static AuthorizationLevel GetAuthorizationLevel(HttpRequestMessage request, SecretManager secretManager, string functionName = null)
        {
            // TODO: Add support for validating "EasyAuth" headers

            // first see if a key value is specified via headers or query string (header takes precidence)
            IEnumerable<string> values;
            string keyValue = null;
            if (request.Headers.TryGetValues(FunctionsKeyHeaderName, out values))
            {
                keyValue = values.FirstOrDefault();
            }
            else
            {
                var queryParameters = request.GetQueryNameValuePairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                queryParameters.TryGetValue("code", out keyValue);
            }

            if (!string.IsNullOrEmpty(keyValue))
            {
                // see if the key specified is the master key
                HostSecretsInfo hostSecrets = secretManager.GetHostSecrets();
                if (!string.IsNullOrEmpty(hostSecrets.MasterKey) &&
                    Key.SecretValueEquals(keyValue, hostSecrets.MasterKey))
                {
                    return AuthorizationLevel.Admin;
                }

                // see if the key specified matches the host function key
                if (hostSecrets.FunctionKeys != null &&
                    hostSecrets.FunctionKeys.Any(k => Key.SecretValueEquals(keyValue, k.Value)))
                {
                    return AuthorizationLevel.Function;
                }

                // if there is a function specific key specified try to match against that
                if (functionName != null)
                {
                    Dictionary<string, string> functionSecrets = secretManager.GetFunctionSecrets(functionName);
                    if (functionSecrets != null &&
                        functionSecrets.Values.Any(s => Key.SecretValueEquals(keyValue, s)))
                    {
                        return AuthorizationLevel.Function;
                    }
                }
            }

            return AuthorizationLevel.Anonymous;
        }
    }
}