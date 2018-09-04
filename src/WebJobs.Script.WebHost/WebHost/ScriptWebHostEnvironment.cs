// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class ScriptWebHostEnvironment : IScriptWebHostEnvironment
    {
        private readonly ReaderWriterLockSlim _delayLock = new ReaderWriterLockSlim();
        private readonly IEnvironment _environment;
        private TaskCompletionSource<object> _delayTaskCompletionSource;
        private bool? _standbyMode;

        public ScriptWebHostEnvironment()
            : this(SystemEnvironment.Instance)
        {
        }

        public ScriptWebHostEnvironment(IEnvironment environment)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _delayTaskCompletionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _delayTaskCompletionSource.SetResult(null);
        }

        public bool DelayRequestsEnabled => !DelayCompletionTask.IsCompleted;

        public Task DelayCompletionTask
        {
            get
            {
                _delayLock.EnterReadLock();
                try
                {
                    return _delayTaskCompletionSource.Task;
                }
                finally
                {
                    _delayLock.ExitReadLock();
                }
            }
        }

        public bool InStandbyMode
        {
            get
            {
                // once set, never reset
                if (_standbyMode != null)
                {
                    return _standbyMode.Value;
                }
                if (_environment.IsPlaceholderModeEnabled())
                {
                    return true;
                }

                // no longer standby mode
                _standbyMode = false;

                return _standbyMode.Value;
            }
        }

        public void DelayRequests()
        {
            _delayLock.EnterUpgradeableReadLock();
            try
            {
                if (_delayTaskCompletionSource.Task.IsCompleted)
                {
                    _delayLock.EnterWriteLock();
                    try
                    {
                        _delayTaskCompletionSource = new TaskCompletionSource<object>();
                    }
                    finally
                    {
                        _delayLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                _delayLock.ExitUpgradeableReadLock();
            }
        }

        public void ResumeRequests()
        {
            _delayLock.EnterReadLock();
            try
            {
                _delayTaskCompletionSource?.SetResult(null);
            }
            finally
            {
                _delayLock.ExitReadLock();
            }
        }

        public void FlagAsSpecializedAndReady()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
        }
    }
}
