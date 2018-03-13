// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Web.Http.ExceptionHandling;
using System.Web.Http.Results;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Newtonsoft.Json;
using ExceptionProcessor = System.Action<System.Web.Http.ExceptionHandling.ExceptionContext,
    Microsoft.Azure.WebJobs.Extensions.Http.AuthorizationLevel, Microsoft.Azure.WebJobs.Script.WebHost.Models.ApiErrorModel>;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class ExceptionProcessingHandler : ExceptionHandler
    {
        private readonly IDictionary<Type, ExceptionProcessor> _handlers;
        private readonly System.Web.Http.HttpConfiguration _config;

        public ExceptionProcessingHandler(System.Web.Http.HttpConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _config = config;
            _handlers = InitializeExceptionHandlers();
        }

        private static IDictionary<Type, ExceptionProcessor> InitializeExceptionHandlers()
        {
            var handlers = new Dictionary<Type, ExceptionProcessor>
            {
                { typeof(CryptographicException), CryptographicExceptionHandler }
            };

            return handlers;
        }

        public override void Handle(ExceptionHandlerContext context)
        {
            var error = new ApiErrorModel(HttpStatusCode.InternalServerError);

            AuthorizationLevel currentLevel = context.Request.GetAuthorizationLevel();
            foreach (var handler in GetExceptionHandlers(context.Exception))
            {
                handler(context.ExceptionContext, currentLevel, error);
            }

            TraceErrorEvent(context.ExceptionContext, error);

            context.Result = new ResponseMessageResult(context.Request.CreateResponse(error.StatusCode, error));
        }

        private IEnumerable<ExceptionProcessor> GetExceptionHandlers(Exception exc)
        {
            if (exc == null)
            {
                yield break;
            }

            // We return our default handler first.
            // Subsequent handlers can override everything done by it.
            yield return DefaultExceptionHandler;

            Type exceptionType = exc.GetType();
            ExceptionProcessor exceptionHandler = null;
            if (exceptionType != null && _handlers.TryGetValue(exceptionType, out exceptionHandler))
            {
                yield return exceptionHandler;
            }
        }

        private static void DefaultExceptionHandler(ExceptionContext exceptionContext, AuthorizationLevel currentLevel, ApiErrorModel error)
        {
            error.RequestId = exceptionContext.Request?.GetRequestId();
            if (currentLevel == AuthorizationLevel.Admin || exceptionContext.RequestContext.IsLocal)
            {
                error.Message = GetExceptionMessage(exceptionContext.Exception);

                if (exceptionContext.RequestContext.IncludeErrorDetail)
                {
                    error.ErrorDetails = ExceptionFormatter.GetFormattedException(exceptionContext.Exception);
                }
            }
            else
            {
                error.Message = $"An error has occurred. For more information, please check the logs for error ID {error.Id}";
            }
        }

        private void TraceErrorEvent(ExceptionContext exceptionContext, ApiErrorModel error)
        {
            string message = JsonConvert.SerializeObject(error);
            var traceEvent = new TraceEvent(TraceLevel.Error, message, $"ApiError", exceptionContext.Exception);

            var traceWriter = _config.DependencyResolver.GetService<TraceWriter>();
            traceWriter.Trace(traceEvent);
        }

        private static void CryptographicExceptionHandler(ExceptionContext exceptionContext, AuthorizationLevel currentLevel, ApiErrorModel error)
        {
            if (currentLevel == AuthorizationLevel.Admin)
            {
                error.ErrorCode = ErrorCodes.KeyCryptographicError;
                error.Message = "Cryptographic error. Unable to encrypt or decrypt keys.";
            }
        }

        private static string GetExceptionMessage(Exception exception)
        {
            if (exception == null)
            {
                return string.Empty;
            }

            var aggregateException = exception as AggregateException;
            if (aggregateException != null)
            {
                exception = aggregateException.Flatten().InnerException;
            }

            var messages = new List<string>();
            while (exception != null)
            {
                messages.Add(exception.Message);
                exception = exception.InnerException;
            }

            return string.Join(" -> ", messages);
        }
    }
}