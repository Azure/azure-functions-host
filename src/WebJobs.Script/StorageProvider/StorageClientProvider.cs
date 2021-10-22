// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Azure.Core;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Passthrough-class to create Azure storage service clients using using <see cref="AzureComponentFactory"/>.
    /// If the connection is not specified, it uses a default account.
    /// </summary>
    internal abstract class StorageClientProvider<TClient, TClientOptions> where TClientOptions : ClientOptions
    {
        private readonly AzureComponentFactory _componentFactory;
        private readonly AzureEventSourceLogForwarder _logForwarder;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageClientProvider{TClient, TClientOptions}"/> class that uses the registered Azure services.
        /// </summary>
        /// <param name="componentFactory">The Azure factory responsible for creating clients. <see cref="AzureComponentFactory"/>.</param>
        /// <param name="logForwarder">Log forwarder that forwards events to ILogger. <see cref="AzureEventSourceLogForwarder"/>.</param>
        public StorageClientProvider(AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder)
        {
            _componentFactory = componentFactory;
            _logForwarder = logForwarder;

            _logForwarder.Start();
        }

        public virtual TClient Create(string name, INameResolver resolver, IConfiguration configuration)
        {
            var resolvedName = resolver.ResolveWholeString(name);
            return this.Create(resolvedName, configuration);
        }

        public virtual TClient Create(string name, IConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ConnectionStringNames.Storage; // default
            }

            IConfigurationSection connectionSection = configuration?.GetWebJobsConnectionSection(name);
            if (connectionSection == null || !connectionSection.Exists())
            {
                // Not found
                throw new InvalidOperationException($"Storage account connection string '{IConfigurationExtensions.GetPrefixedConnectionStringName(name)}' does not exist. Make sure that it is a defined App Setting.");
            }

            var credential = _componentFactory.CreateTokenCredential(connectionSection);
            var options = CreateClientOptions(connectionSection);
            return CreateClient(connectionSection, credential, options);
        }

        protected virtual TClient CreateClient(IConfiguration configuration, TokenCredential tokenCredential, TClientOptions options)
        {
            return (TClient)_componentFactory.CreateClient(typeof(TClient), configuration, tokenCredential, options);
        }

        /// <summary>
        /// Checks if the specified <see cref="IConfiguration"/> object has a value. This is assumed to be a connection string.
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> to retrieve the value from.</param>
        /// <returns>true if this <see cref="IConfiguration"/> object is a connection string; false otherwise.</returns>
        protected bool IsConnectionStringPresent(IConfiguration configuration)
        {
            return configuration is IConfigurationSection section && section.Value != null;
        }

        private TClientOptions CreateClientOptions(IConfiguration configuration)
        {
            var clientOptions = (TClientOptions)_componentFactory.CreateClientOptions(typeof(TClientOptions), null, configuration);
            return clientOptions;
        }
    }
}
