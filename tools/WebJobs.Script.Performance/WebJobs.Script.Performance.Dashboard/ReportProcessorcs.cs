using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public class ReportProcessor
    {
        private static Dictionary<string, double> RPSThreshold = new Dictionary<string, double>()
        {
            { "C# Ping (VS)", 130},
            { "Java Ping", 60},
            { "JS Ping", 80},
            { "C# Ping", 130},
            { "PS Ping", 60}
        };

        public static async Task<string> GetLastDaysHtmlReport(int days, bool onlyWarnings)
        {
            string tableContent = string.Empty;
            for (int i = 0; i < days; i++)
            {
                DateTime startDate = DateTime.UtcNow.AddDays(-i);
                tableContent += await ReportProcessor.GetHtmlReport(startDate.Year.ToString("d2"), startDate.Month.ToString("d2"), startDate.Day.ToString("d2"), onlyWarnings);
            }
            return tableContent;
        }

        public static async Task<string> GetHtmlReport(string year, string month, string day, bool onlyWarnings)
        {
            string content = string.Empty;
            year = year ?? DateTime.Now.Year.ToString();
            month = month ?? DateTime.Now.Month.ToString("d2");
            string blobPrefix = $"{year}-{month}";

            if (!string.IsNullOrEmpty(day))
            {
                blobPrefix = $"{blobPrefix}-{day}";
            }

            string blobConectionString = Environment.GetEnvironmentVariable("PerfStorage", EnvironmentVariableTarget.Process);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(blobConectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("dashboard");

            BlobResultSegment result = await container.ListBlobsSegmentedAsync(blobPrefix, null);
            foreach (var item in result.Results.OrderByDescending(x => x.StorageUri.ToString()))
            {
                string blobUri = item.Uri.ToString().TrimEnd('/');
                string time = blobUri.Split('/').Last().Replace("-", "/").Replace("_", ":");
                try
                {
                    CloudBlockBlob reportBlob = container.GetBlockBlobReference(blobUri.Split('/').Last() + "/summary.txt");
                    string summary = await reportBlob.DownloadTextAsync();
                    string[] summaryNumbers = summary.Split(',');
                    int totalRequests = int.Parse(summaryNumbers[2]);
                    double avgResponseTime = Math.Round(double.Parse(summaryNumbers[5]), 2);
                    string testName = summaryNumbers[0];
                    string runtime = summaryNumbers[1];
                    RPSThreshold.TryGetValue(testName, out double rpsThreshold);
                    string rpsThresholdString = rpsThreshold != 0 ? $" (Threshold:{RPSThreshold[testName].ToString()})" : string.Empty;
                    double rps = totalRequests / 120;
                    string rpsString = $"{Math.Round(rps, 2).ToString()}{rpsThresholdString}";

                    string style = string.Empty;
                    if ((rpsThreshold != 0) && (rps < rpsThreshold))
                    {
                        style = " style='background:yellow'";
                    }

                    string row = $"<tr{style}><td>{time}</td><td><a href='{Uri.EscapeUriString(blobUri)}/index.html'>{testName}</a></td><td>{runtime}</td><td>{rpsString}</td><td>{totalRequests}</td><td>{avgResponseTime}</td></tr>";
                    if (onlyWarnings)
                    {
                        if(!string.IsNullOrEmpty(style))
                        {
                            content += row;
                        }
                    }
                    else
                    {
                        content += row;
                    }
                }
                catch (Exception)
                {
                    // The test run is failed
                    content += $"<tr style='background:yellow'><td>{time}</td><td>Failed</td><td></td><td></td><td></td><td></td></tr>";
                }
            }

            return content;
        }
    }
}
