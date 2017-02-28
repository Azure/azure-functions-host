// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Description;

namespace Microsoft.Azure.WebJobs.Host.Config
{
    /// <summary>
    /// Context object passed to <see cref="IExtensionConfigProvider"/> instances when
    /// they are initialized.
    /// </summary>
    public class ExtensionConfigContext : FluentConverterRules<Attribute, ExtensionConfigContext>
    {
        // List of actions to flush from the fluent configuration. 
        private List<Action> _updates = new List<Action>();

        // track which TAttribute rules have been added. 
        private HashSet<object> _existingRules = new HashSet<object>();

        internal IExtensionConfigProvider Current { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="JobHostConfiguration"/>.
        /// </summary>
        public JobHostConfiguration Config { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="TraceWriter"/>.
        /// </summary>
        public TraceWriter Trace { get; set; }

        internal override IConverterManager Converters
        {
            get
            {
                return this.Config.ConverterManager;
            }
        }

        internal ServiceProviderWrapper PerHostServices { get; set; }

        /// <summary>
        /// Get a fully qualified URL that the host will resolve to this extension 
        /// </summary>
        /// <returns>null if http handlers are not supported in this environment</returns>
        [Obsolete("preview")]
        public Uri GetWebhookHandler()
        {
            var webhook = this.Config.GetService<IWebHookProvider>();
            if (webhook == null)
            {
                return null;
            }            
            return webhook.GetUrl(this.Current);            
        }

        /// <summary>
        /// Add a binding rule for the given attribute
        /// </summary>
        /// <typeparam name="TAttribute"></typeparam>
        /// <returns></returns>
        public FluentBindingRule<TAttribute> AddBindingRule<TAttribute>() where TAttribute : Attribute
        {
            bool hasBindingAttr = typeof(TAttribute).GetCustomAttributes(typeof(BindingAttribute), false).Length > 0;
            if (!hasBindingAttr)
            {                
                throw new InvalidOperationException($"Can't add a binding rule for '{typeof(TAttribute).Name}' since it is missing the a {typeof(BindingAttribute).Name}");
            }

            if (!_existingRules.Add(typeof(TAttribute)))
            {
                throw new InvalidOperationException($"Only call AddBindingRule once per attribute type.");
            }

            var fluent = new FluentBindingRule<TAttribute>(this.Config);
            _updates.Add(fluent.ApplyRules);
            return fluent;
        }

        // Called after we return from the extension's intitialize code. 
        // This will apply the rules and update the config. 
        internal void ApplyRules()
        {
            foreach (var func in _updates)
            {
                func();
            }
            _updates.Clear();
        }
    }    
}
