using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.WindowsAzure.ServiceRuntime;
using Newtonsoft.Json;

namespace WorkerRole1
{
    // Pings a webservice to provide tracking statistics. 
    public static class Recorder
    {
        static ExecutionNodeTrackingStats _stats;
        static string _url;

        public static void Start(string accountName)
        {
            string urlBase = RoleEnvironment.GetConfigurationSettingValue("StatsTrackerUrl");

            if (string.IsNullOrWhiteSpace(urlBase))
            {
                // Tracking is disabled.
                return;
            }

            string deployId = RoleEnvironment.DeploymentId + "," + RoleEnvironment.CurrentRoleInstance.Id;
            

            _url = urlBase + @"/api/UsageStats";

            // IDeally, we'd collect Azure VM size, but that's not avaiable. 
            // But we can infer it from # of cores and cpu speed.
            _stats = new ExecutionNodeTrackingStats
            {
                PhysicalMemory = MemoryStats.GetMemory(),
                NumCores = Environment.ProcessorCount,
                OSVersion = Environment.OSVersion.ToString(),
                AccountName = accountName, // provides identity
                DeploymentId = deployId // this is a guid if on real azure.  
            };

            Thread t = new Thread(RecordWorker);
            t.Start();
        }

        static void RecordWorker(object o)
        {
            while (true)
            {
                Record();

                var milliseconds = 1000 * 60 * 60; // 1 hour
                Thread.Sleep(milliseconds);
            }
        }

        static void Record()
        {
            try
            {
                PostJson(_url, _stats);
            }
            catch
            {
                // If service is unavailable, not fatal.
            }
        }

        static void PostJson(string url, object body)
        {
            var json = JsonConvert.SerializeObject(body);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            WebRequest request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length; // set before writing to stream
            var stream = request.GetRequestStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Close();

            var response = request.GetResponse(); // does the actual web request
        }

        // $$$ Merge with Monitor
        public class ExecutionNodeTrackingStats
        {
            public int NumCores { get; set; }
            public ulong PhysicalMemory { get; set; }
            public string OSVersion { get; set; }
            public string AccountName { get; set; }
            public string DeploymentId { get; set; }
        }
    }

    class MemoryStats
    {
        // http://stackoverflow.com/questions/105031/how-do-you-get-total-amount-of-ram-the-computer-has
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static ulong GetMemory()
        {
            ulong installedMemory;
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                installedMemory = memStatus.ullTotalPhys;

                return memStatus.ullTotalPhys;
            }
            return 0;
        }
    }
}