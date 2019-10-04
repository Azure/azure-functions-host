// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCrontab;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// BindingProvider for the core WebJobs Extensions
    /// </summary>
    internal class CoreExtensionsScriptBindingProvider : ScriptBindingProvider
    {
        private readonly INameResolver _nameResolver;
        private readonly IEnvironment _environment;

        public CoreExtensionsScriptBindingProvider(INameResolver nameResolver, IEnvironment environment, ILogger<CoreExtensionsScriptBindingProvider> logger)
            : base(logger)
        {
            _nameResolver = nameResolver ?? throw new ArgumentNullException(nameof(nameResolver));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <inheritdoc/>
        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            binding = null;

            if (string.Equals(context.Type, "timerTrigger", StringComparison.OrdinalIgnoreCase))
            {
                binding = new TimerTriggerScriptBinding(_nameResolver, _environment, context);
            }

            return binding != null;
        }

        internal class TimerTriggerScriptBinding : ScriptBinding
        {
            private readonly INameResolver _nameResolver;
            private readonly IEnvironment _environment;

            public TimerTriggerScriptBinding(INameResolver nameResolver, IEnvironment environment, ScriptBindingContext context) : base(context)
            {
                _nameResolver = nameResolver ?? throw new ArgumentNullException(nameof(nameResolver));
                _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            }

            public override Type DefaultType => typeof(TimerInfo);

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                string schedule = Context.GetMetadataValue<string>("schedule");
                bool runOnStartup = Context.GetMetadataValue<bool>("runOnStartup");
                bool useMonitor = Context.GetMetadataValue<bool>("useMonitor", true);
                if (_environment.IsWindowsConsumption())
                {
                    // pre-resolve app setting specifiers
                    schedule = _nameResolver.ResolveWholeString(schedule);

                    var options = new CrontabSchedule.ParseOptions()
                    {
                        IncludingSeconds = true
                    };
                    if (CrontabSchedule.TryParse(schedule, options) == null)
                    {
                        throw new ArgumentException(string.Format("'{0}' is not a valid CRON expression.", schedule));
                    }
                }

                attributes.Add(new TimerTriggerAttribute(schedule)
                {
                    RunOnStartup = runOnStartup,
                    UseMonitor = useMonitor
                });

                return attributes;
            }
        }
    }
}