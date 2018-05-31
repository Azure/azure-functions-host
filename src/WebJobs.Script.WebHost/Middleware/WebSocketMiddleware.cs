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
                IWebSocketConsumer extension = provider.GetWebSocketConsumerOrNull(name);
                DelayedFunctionExecution functionExecution = await extension.GetFunctionExecutionAsync(websocket, token);
                await functionExecution.ExecuteAsync(token);
                // The websocket currently closes with an exception. There is no harm caused by the exception at this point in the execution pipepine.
                // It seems connected to https://github.com/aspnet/AspNetCoreModule/issues/77 so we can test it without the try-catch after upgrading
                // to .NET Core 2.1.
                try
                {
                    await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "OK", token);
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Websocket close encountered this exception");
                }
            }
            catch (Exception ex)
            {
                await websocket.CloseAsync(WebSocketCloseStatus.InternalServerError, ex.ToString(), token);
                _logger.LogError(ex, "Websocket triggered execution failed.");
            }
            finally
            {
                websocket.Dispose();
            }
        }
    }
}
