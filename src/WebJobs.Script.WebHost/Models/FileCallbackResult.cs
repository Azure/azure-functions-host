// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

// https://github.com/StephenClearyExamples/AsyncDynamicZip/tree/core-ziparchive
namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// Represents an <see cref="ActionResult"/> that when executed will
    /// execute a callback to write the file content out as a stream.
    /// </summary>
    public class FileCallbackResult : FileResult
    {
        private Func<Stream, ActionContext, Task> _callback;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileCallbackResult"/> class.
        /// </summary>
        /// <param name="contentType">The Content-Type header of the response.</param>
        /// <param name="callback">The stream with the file.</param>
        public FileCallbackResult(string contentType, Func<Stream, ActionContext, Task> callback)
            : this(MediaTypeHeaderValue.Parse(contentType), callback)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileCallbackResult"/> class.
        /// </summary>
        /// <param name="contentType">The Content-Type header of the response.</param>
        /// <param name="callback">The stream with the file.</param>
        public FileCallbackResult(MediaTypeHeaderValue contentType, Func<Stream, ActionContext, Task> callback)
            : base(contentType?.ToString())
        {
            ArgumentNullException.ThrowIfNull(callback);

            Callback = callback;
        }

        /// <summary>
        /// Gets or sets the callback responsible for writing the file content to the output stream.
        /// </summary>
        public Func<Stream, ActionContext, Task> Callback
        {
            get
            {
                return _callback;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _callback = value;
            }
        }

        /// <inheritdoc />
        public override Task ExecuteResultAsync(ActionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var executor = new FileCallbackResultExecutor(context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>());
            return executor.ExecuteAsync(context, this);
        }

        private sealed class FileCallbackResultExecutor : FileResultExecutorBase
        {
            public FileCallbackResultExecutor(ILoggerFactory loggerFactory)
                : base(CreateLogger<FileCallbackResultExecutor>(loggerFactory))
            {
            }

            public Task ExecuteAsync(ActionContext context, FileCallbackResult result)
            {
                SetHeadersAndLog(context, result, null, true);
                return result.Callback(context.HttpContext.Response.Body, context);
            }
        }
    }
}