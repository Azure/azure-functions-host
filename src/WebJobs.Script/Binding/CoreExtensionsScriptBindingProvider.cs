// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using NCrontab;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// BindingProvider for the core WebJobs Extensions
    /// </summary>
    internal class CoreExtensionsScriptBindingProvider : ScriptBindingProvider
    {
        public CoreExtensionsScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
            : base(config, hostMetadata, traceWriter)
        {
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            Config.UseTimers();
            Config.UseCore();
        }

        /// <inheritdoc/>
        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            binding = null;

            if (string.Compare(context.Type, "timerTrigger", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new TimerTriggerScriptBinding(context);
            }

            return binding != null;
        }

        internal class TimerTriggerScriptBinding : ScriptBinding
        {
            public TimerTriggerScriptBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    return typeof(TimerInfo);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                string schedule = Context.GetMetadataValue<string>("schedule");
                bool runOnStartup = Context.GetMetadataValue<bool>("runOnStartup");
                bool useMonitor = Context.GetMetadataValue<bool>("useMonitor", true);

                if (Utility.IsDynamic)
                {
                    // pre-resolve app setting specifiers
                    var resolver = new DefaultNameResolver();
                    schedule = resolver.ResolveWholeString(schedule);

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