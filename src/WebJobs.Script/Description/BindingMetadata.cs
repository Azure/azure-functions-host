﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Represents the metadata for a function parameter binding.
    /// </summary>
    public class BindingMetadata
    {
        /// <summary>
        /// Gets or sets the name of the binding.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets an app setting name for a setting whose value
        /// contains the connection details that should be used for this binding.
        /// E.g. for a Queue binding, this might be set to the name of a an app
        /// setting containing the Azure Storage connection string to use.
        /// </summary>
        public string Connection { get; set; }

        /// <summary>
        /// Gets or sets the type of the binding.
        /// </summary>
        public BindingType Type { get; set; }

        /// <summary>
        /// Gets or sets the direction of the binding.
        /// </summary>
        public BindingDirection Direction { get; set; }

        /// <summary>
        /// Gets a value indicating whether this binding is a trigger binding.
        /// </summary>
        public bool IsTrigger
        {
            get
            {
                return
                    Type == BindingType.TimerTrigger ||
                    Type == BindingType.BlobTrigger ||
                    Type == BindingType.HttpTrigger ||
                    Type == BindingType.QueueTrigger ||
                    Type == BindingType.EventHubTrigger ||
                    Type == BindingType.ServiceBusTrigger ||
                    Type == BindingType.ManualTrigger ||
                    Type == BindingType.ApiHubTrigger;
            }
        }

        /// <summary>
        /// Bindings can include information that drives the JobHostConfiguration.
        /// </summary>
        /// <param name="configBuilder"></param>
        public virtual void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            // default is nop
        }        
    }
}
