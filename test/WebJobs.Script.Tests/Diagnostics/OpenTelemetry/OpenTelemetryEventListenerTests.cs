﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.OpenTelemetry
{
    public class OpenTelemetryEventListenerTests : IDisposable
    {
        private readonly OpenTelemetryEventListener listener = new OpenTelemetryEventListener(EventLevel.Informational);
        private CoreEventSource log = new CoreEventSource();

        public void Dispose()
        {
            listener.Dispose();
            log.Dispose();
        }

        [Fact]
        public async Task TestEventHandling()
        {
            DiagnosticListener.AllListeners.Subscribe(new TestDiagnosticObserver());
            for (int i = 0; i < 40; i++)
            {
                log.WarningEvent("Logging warning event");
                log.VerboseEvent("Logging verbose event");
            }
            await Task.Delay(600);
            Assert.Equal(true, ValidateEvent.Assert);
        }
    }

    [EventSource(Name = "OpenTelemetry-CoreTest")]
    internal sealed class CoreEventSource : EventSource
    {
        public static readonly CoreEventSource Log = new CoreEventSource();

        [Event(1, Message = "WarningMessage", Level = EventLevel.Warning)]
        public void WarningEvent(string message)
        {
            this.WriteEvent(1, message);
        }

        [Event(2, Message = "VerboseMessage", Level = EventLevel.Verbose)]
        public void VerboseEvent(string message)
        {
            this.WriteEvent(2, message);
        }
    }

    public class TestDiagnosticObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "Microsoft.Azure.Functions.Host.OpenTelemetry")
            {
                value.Subscribe(new TestKeyValueObserver());
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }
    }

    public class TestKeyValueObserver : IObserver<KeyValuePair<string, object>>
    {
        public void OnNext(KeyValuePair<string, object> value)
        {
            ValidateEvent.Assert = ValidateEvent.Validate(value);
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }
    }

    public static class ValidateEvent
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:Fields should be private", Justification = "<Pending>")]
        public static bool Assert = false;

        public static bool Validate(KeyValuePair<string, object> value)
        {
            // Validate event listener
            if (value.Key != "OpenTelemetryEventListener")
            {
                return false;
            }

            // message is not null
            if (value.Value == null)
            {
                return false;
            }

            // verbose messages are filtered out
            if (value.Value.ToString().Contains("VerboseMessage"))
            {
                return false;
            }

            int count = value.Value.ToString().Count(c => c == '\n');
            // 35 messages and 1 empty line
            if (count != 36)
            {
                return false;
            }
            return true;
        }
    }
}