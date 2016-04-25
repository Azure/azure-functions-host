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
            string keyId = null;
            if (request.Headers.TryGetValues(FunctionsKeyHeaderName, out values))
            {
                // TODO: also allow keyId to be specified via header

                keyValue = values.FirstOrDefault();
            }
            else
            {
                var queryParameters = request.GetQueryNameValuePairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                queryParameters.TryGetValue("code", out keyValue);
                queryParameters.TryGetValue("id", out keyId);
            }

            // if a key has been specified on the request, validate it
            if (!string.IsNullOrEmpty(keyValue))
            {
                HostSecrets hostSecrets = secretManager.GetHostSecrets();
                if (hostSecrets != null)
                {
                    // see if the key specified matches the master key
                    if (SecretEqual(keyValue, hostSecrets.MasterKey))
                    {
                        return AuthorizationLevel.Admin;
                    }

                    // see if the key specified matches the host function key
                    if (SecretEqual(keyValue, hostSecrets.FunctionKey))
                    {
                        return AuthorizationLevel.Function;
                    }
                }

                // see if the specified key matches the function specific key
                if (functionName != null)
                {
                    FunctionSecrets functionSecrets = secretManager.GetFunctionSecrets(functionName);
                    if (functionSecrets != null &&
                        SecretEqual(keyValue, functionSecrets.GetKeyValue(keyId)))
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

            if (string.IsNullOrEmpty(inputA) || string.IsNullOrEmpty(inputB) || 
                inputA.Length != inputB.Length)
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