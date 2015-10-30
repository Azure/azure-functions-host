// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// This class defines a strategy used for processing ServiceBus messages.
    /// </summary>
    /// <remarks>
    /// Custom <see cref="MessageProcessor"/> implementations can be specified by implementing
    /// a custom <see cref="MessagingProvider"/> and setting it via <see cref="ServiceBusConfiguration.MessagingProvider"/>.
    /// </remarks>
    public class MessageProcessor
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="messageOptions">The <see cref="OnMessageOptions"/> to use.</param>
        public MessageProcessor(OnMessageOptions messageOptions)
        {
            if (messageOptions == null)
            {
                throw new ArgumentNullException("messageOptions");
            }

            MessageOptions = messageOptions;
        }

        /// <summary>
        /// Gets the <see cref="OnMessageOptions"/> that will be used by the message receiver.
        /// </summary>
        public OnMessageOptions MessageOptions { get; protected set; }

        /// <summary>
        /// This method is called when there is a new message to process, before the job function is invoked.
        /// This allows any preprocessing to take place on the message before processing begins.
        /// </summary>
        /// <param name="message">The message to process.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use.</param>
        /// <returns>A <see cref="Task"/> that returns true if the message processing should continue, false otherwise.</returns>
        public virtual async Task<bool> BeginProcessingMessageAsync(BrokeredMessage message, CancellationToken cancellationToken)
        {
            return await Task.FromResult<bool>(true);
        }

        /// <summary>
        /// This method completes processing of the specified message, after the job function has been invoked.
        /// </summary>
        /// <param name="message">The message to complete processing for.</param>
        /// <param name="result">The <see cref="FunctionResult"/> from the job invocation.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use</param>
        /// <returns>A <see cref="Task"/> that will complete the message processing.</returns>
        public virtual async Task CompleteProcessingMessageAsync(BrokeredMessage message, FunctionResult result, CancellationToken cancellationToken)
        {
            if (result.Succeeded)
            {
                if (!MessageOptions.AutoComplete)
                {
                    // AutoComplete is true by default, but if set to false
                    // we need to complete the message
                    cancellationToken.ThrowIfCancellationRequested();
                    await message.CompleteAsync();
                }
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                await message.AbandonAsync();
            }
        }
    }
}
