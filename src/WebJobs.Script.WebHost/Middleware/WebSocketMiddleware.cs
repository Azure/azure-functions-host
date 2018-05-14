// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class WebSocketMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly ILogger<WebSocketMiddleware> _logger;

        public WebSocketMiddleware(RequestDelegate next, WebScriptHostManager manager, ILoggerFactory loggerFactory)
        {
            _next = next;
            _scriptHostManager = manager;
            _logger = loggerFactory.CreateLogger<WebSocketMiddleware>();
        }

        public async Task Invoke(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                await _next.Invoke(context);
                return;
            }

            string name = context.Request.Query["extension"];

            CancellationToken token = context.RequestAborted;
            WebSocket websocket = await context.WebSockets.AcceptWebSocketAsync();
            var provider = _scriptHostManager.BindingWebHookProvider;
            try
            {
                IWebSocketExtension extension = provider.GetWebSocketExtensionOrNull(name);
                DelayedFunctionExecution functionExecution = await extension.GetWebSocketFunctionAsync(websocket);
                await functionExecution.Execute();
                await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "OK", token);
            }
            catch (Exception ex)
            {
                await websocket.CloseAsync(WebSocketCloseStatus.InternalServerError, ex.ToString(), token);
                _logger.LogError(ex.StackTrace);
            }
            finally
            {
                websocket.Dispose();
            }
        }
    }
}
