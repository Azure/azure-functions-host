// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Net.Mail;
using System.Reflection;
using Microsoft.Azure.WebJobs.Extensions.SendGrid;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// BindingProvider for SendGrid extensions
    /// </summary>
    internal class SendGridScriptBindingProvider : ScriptBindingProvider
    {
        /// <inheritdoc/>
        public SendGridScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
            : base(config, hostMetadata, traceWriter)
        {
        }

        /// <inheritdoc/>
        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            binding = null;

            if (string.Compare(context.Type, "sendGrid", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new SendGridBinding(context);
            }

            return binding != null;
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            var sendGridConfig = CreateConfiguration(Metadata);
            Config.UseSendGrid(sendGridConfig);
        }

        /// <inheritdoc/>
        public override bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;

            return Utility.TryMatchAssembly(assemblyName, typeof(SendGridAPIClient), out assembly) ||
                   Utility.TryMatchAssembly(assemblyName, typeof(SendGridAttribute), out assembly);
        }

        internal static SendGridConfiguration CreateConfiguration(JObject metadata)
        {
            SendGridConfiguration sendGridConfig = new SendGridConfiguration();

            JObject configSection = (JObject)metadata.GetValue("sendGrid", StringComparison.OrdinalIgnoreCase);
            JToken value = null;
            if (configSection != null)
            {
                Email mailAddress = null;
                if (configSection.TryGetValue("from", StringComparison.OrdinalIgnoreCase, out value) &&
                    TryParseAddress((string)value, out mailAddress))
                {
                    sendGridConfig.FromAddress = mailAddress;
                }

                if (configSection.TryGetValue("to", StringComparison.OrdinalIgnoreCase, out value) &&
                    TryParseAddress((string)value, out mailAddress))
                {
                    sendGridConfig.ToAddress = mailAddress;
                }
            }

            return sendGridConfig;
        }

        internal static bool TryParseAddress(string value, out Email email)
        {
            email = null;

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            try
            {
                // MailAddress will auto-parse the name from a string like "testuser@test.com <Test User>"
                MailAddress mailAddress = new MailAddress(value);
                string displayName = string.IsNullOrEmpty(mailAddress.DisplayName) ? null : mailAddress.DisplayName;
                email = new Email(mailAddress.Address, displayName);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private class SendGridBinding : ScriptBinding
        {
            public SendGridBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    return typeof(IAsyncCollector<JObject>);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                return new Collection<Attribute>
                {
                    new SendGridAttribute
                    {
                        ApiKey = Context.GetMetadataValue<string>("apiKey"),
                        To = Context.GetMetadataValue<string>("to"),
                        From = Context.GetMetadataValue<string>("from"),
                        Subject = Context.GetMetadataValue<string>("subject"),
                        Text = Context.GetMetadataValue<string>("text")
                    }
                };
            }
        }
    }
}
