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
using Microsoft.Azure.WebJobs.Script;

namespace WebJobs.Script.WebHost.Filters
{
    public class AuthorizationLevelAttribute : AuthorizationFilterAttribute
    {
        public const string FunctionsKeyHeaderName = "x-functions-key";

        public AuthorizationLevelAttribute(AuthorizationLevel level)
        {
            Level = level;
        }

        public AuthorizationLevel Level { get; private set; }

        public override void OnAuthorization(HttpActionContext actionContext)
        {
            SecretManager secretManager = (SecretManager)actionContext.ControllerContext.Configuration.DependencyResolver.GetService(typeof(SecretManager));

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
                queryParameters.TryGetValue("key", out keyValue);
            }

            if (!string.IsNullOrEmpty(keyValue))
            {
                // see if the key specified is the master key
                HostSecrets hostSecrets = secretManager.GetHostSecrets();
                if (!string.IsNullOrEmpty(hostSecrets.MasterKey) &&
                    SecretEqual(keyValue, hostSecrets.MasterKey))
                {
                    return AuthorizationLevel.Admin;
                }

                // see if the key specified matches the host function key
                if (!string.IsNullOrEmpty(hostSecrets.FunctionKey) &&
                    SecretEqual(keyValue, hostSecrets.FunctionKey))
                {
                    return AuthorizationLevel.Function;
                }

                // if there is a function specific key specified try to match against that
                if (functionName != null)
                {
                    FunctionSecrets functionSecrets = secretManager.GetFunctionSecrets(functionName);
                    if (functionSecrets != null &&
                        !string.IsNullOrEmpty(functionSecrets.Key) &&
                        SecretEqual(keyValue, functionSecrets.Key))
                    {
                        return AuthorizationLevel.Function;
                    }
                }
            }

            return AuthorizationLevel.Anonymous;
        }

        /// <summary>
        /// Provides a time consistent comparison of two secrets in the form of two strings.
        /// This prevents security attacks that attempt to determine key values based on response
        /// times.
        /// </summary>
        /// <param name="inputA">The first secret to compare.</param>
        /// <param name="inputB">The second secret to compare.</param>
        /// <returns>Returns <c>true</c> if the two secrets are equal, <c>false</c> otherwise.</returns>
        [MethodImpl(MethodImplOptions.NoOptimization)]
        private static bool SecretEqual(string inputA, string inputB)
        {
            if (ReferenceEquals(inputA, inputB))
            {
                return true;
            }

            if (inputA == null || inputB == null || inputA.Length != inputB.Length)
            {
                return false;
            }

            bool areSame = true;
            for (int i = 0; i < inputA.Length; i++)
            {
                areSame &= inputA[i] == inputB[i];
            }

            return areSame;
        }
    }
}