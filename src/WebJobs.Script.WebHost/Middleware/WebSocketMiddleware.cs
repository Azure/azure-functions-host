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
            var provider = _scriptHostManager.BindingWebHookProvider;
            IWebSocketConsumer extension = provider.GetWebSocketConsumerOrNull(name);
            if (extension != null)
            {
                WebSocket websocket = await context.WebSockets.AcceptWebSocketAsync();
                CancellationToken token = context.RequestAborted;
                try
                {
                    DelayedFunctionExecution functionExecution = await extension.GetFunctionExecutionAsync(websocket, token);
                    var functionResult = await functionExecution.ExecuteAsync(token);
                    if (functionResult.Succeeded)
                    {
                        await CloseWebSocketAsync(websocket, WebSocketCloseStatus.NormalClosure, "Function execution finished");
                    } else
                    {
                        await CloseWebSocketAsync(websocket, WebSocketCloseStatus.InternalServerError, null);
                        _logger.LogError("Function executed by websocket failed it's execution.");
                    }
                }
                catch (Exception ex)
                {
                    await CloseWebSocketAsync(websocket, WebSocketCloseStatus.InternalServerError, null);
                    _logger.LogError(ex, "Websocket triggered execution failed.");
                }
                finally
                {
                    websocket.Dispose();
                }
            }
        }

        private async Task CloseWebSocketAsync(WebSocket socket, WebSocketCloseStatus status, string message)
        {
            // The websocket currently closes with an exception. There is no harm caused by the exception at this point in the execution pipepine.
            // It seems connected to https://github.com/aspnet/AspNetCoreModule/issues/77 so we can test it without the try-catch after upgrading
            // to .NET Core 2.1.
            try
            {
                await socket.CloseAsync(status, message, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Websocket close encountered this exception");
            }
        }
    }
}
