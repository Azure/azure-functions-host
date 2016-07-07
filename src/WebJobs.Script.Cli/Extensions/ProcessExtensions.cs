// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WebJobs.Script.Cli.NativeMethods;

namespace WebJobs.Script.Cli.Extensions
{
    internal static class ProcessExtensions
    {
        // http://stackoverflow.com/a/19104345
        public static Task<int> WaitForExitAsync(this Process process, CancellationToken cancellationToken = default(CancellationToken))
        {
            var tcs = new TaskCompletionSource<int>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(process.ExitCode);
            if (cancellationToken != default(CancellationToken))
            {
                cancellationToken.Register(tcs.SetCanceled);
            }

            return tcs.Task;
        }

        // http://blogs.msdn.com/b/bclteam/archive/2006/06/20/640259.aspx
        // http://stackoverflow.com/questions/394816/how-to-get-parent-process-in-net-in-managed-way
        // https://github.com/projectkudu/kudu/blob/master/Kudu.Core/Infrastructure/ProcessExtensions.cs
        public static IEnumerable<Process> GetChildren(this Process process, bool recursive = true)
        {
            int pid = process.Id;
            Dictionary<int, List<int>> tree = GetProcessTree();
            return GetChildren(pid, tree, recursive).Select(cid => SafeGetProcessById(cid)).Where(p => p != null);
        }

        public static Process GetParentProcess(this Process process)
        {
            IntPtr processHandle;
            if (!process.TryGetProcessHandle(out processHandle))
            {
                return null;
            }

            var pbi = new ProcessNativeMethods.ProcessInformation();
            try
            {
                int returnLength;
                int status = ProcessNativeMethods.NtQueryInformationProcess(processHandle, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength);
                if (status != 0)
                {
                    throw new Win32Exception(status);
                }

                return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
            }
            catch
            {
                return null;
            }
        }

        // recursively get children.
        // return depth-first (leaf child first).
        private static IEnumerable<int> GetChildren(int pid, Dictionary<int, List<int>> tree, bool recursive)
        {
            List<int> children;
            if (tree.TryGetValue(pid, out children))
            {
                List<int> result = new List<int>();
                foreach (int id in children)
                {
                    if (recursive)
                    {
                        result.AddRange(GetChildren(id, tree, recursive));
                    }
                    result.Add(id);
                }
                return result;
            }
            return Enumerable.Empty<int>();
        }

        private static Dictionary<int, List<int>> GetProcessTree()
        {
            var tree = new Dictionary<int, List<int>>();
            foreach (var proc in Process.GetProcesses())
            {
                Process parent = proc.GetParentProcess();
                if (parent != null)
                {
                    List<int> children = null;
                    if (!tree.TryGetValue(parent.Id, out children))
                    {
                        tree[parent.Id] = children = new List<int>();
                    }

                    children.Add(proc.Id);
                }
            }

            return tree;
        }

        private static bool TryGetProcessHandle(this Process process, out IntPtr processHandle)
        {
            try
            {
                // This may fail due to access denied.
                // Handle the exception to reduce noises in trace errors.
                processHandle = process.Handle;
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 5)
                {
                    throw;
                }

                processHandle = IntPtr.Zero;
            }

            return processHandle != IntPtr.Zero;
        }

        private static Process SafeGetProcessById(int pid)
        {
            try
            {
                return Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                // Process with an Id is not running.
                return null;
            }
        }
    }
}
