// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Reactive.Subjects;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class ScriptEventManager : IScriptEventManager, IDisposable
    {
        private readonly Subject<ScriptEvent> _subject = new Subject<ScriptEvent>();
        private readonly ConcurrentDictionary<(string, Type), object> _workerState = new();

        private bool _disposed = false;

        public void Publish(ScriptEvent scriptEvent)
        {
            ThrowIfDisposed();

            _subject.OnNext(scriptEvent);
        }

        public IDisposable Subscribe(IObserver<ScriptEvent> observer)
        {
            ThrowIfDisposed();

            return _subject.Subscribe(observer);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ScriptEventManager));
            }
        }

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

        bool IScriptEventManager.TryAddWorkerState<T>(string workerId, T state)
        {
            var key = (workerId, typeof(T));
            return _workerState.TryAdd(key, state);
        }

        bool IScriptEventManager.TryGetWorkerState<T>(string workerId, out T state)
        {
            var key = (workerId, typeof(T));
            if (_workerState.TryGetValue(key, out var tmp) && tmp is T typed)
            {
                state = typed;
                return true;
            }
            state = default;
            return false;
        }

        bool IScriptEventManager.TryRemoveWorkerState<T>(string workerId, out T state)
        {
            var key = (workerId, typeof(T));
            if (_workerState.TryRemove(key, out var tmp) && tmp is T typed)
            {
                state = typed;
                return true;
            }
            state = default;
            return false;
        }
    }
}
