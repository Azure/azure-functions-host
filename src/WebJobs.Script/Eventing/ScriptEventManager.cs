// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reactive.Subjects;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public sealed class ScriptEventManager : IScriptEventManager, IDisposable
    {
        private readonly Subject<ScriptEvent> _subject = new Subject<ScriptEvent>();
        private bool _disposed = false;

        public void Publish(ScriptEvent scriptEvent) => _subject.OnNext(scriptEvent);

        public IDisposable Subscribe(IObserver<ScriptEvent> observer) => _subject.Subscribe(observer);

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _subject.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose() => Dispose(true);
    }
}
