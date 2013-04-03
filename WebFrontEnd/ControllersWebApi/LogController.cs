using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DaasEndpoints;
using DataAccess;
using RunnerInterfaces;

namespace WebFrontEnd.ControllersWebApi
{
    public class LogController : ApiController
    {
        private static Services GetServices()
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);
        }

        [HttpGet]
        public HttpResponseMessage GetFunctionLog(int N = 20, string account=null)
        {
            LogAnalysis l = new LogAnalysis();
            IFunctionInstanceQuery query = GetServices().GetFunctionInstanceQuery();
            IEnumerable<ChargebackRow> logs = l.GetChargebackLog(N, account, query);
            
            using (var tw = new StringWriter())
            {
                tw.WriteLine("Name, Id, ParentId, GroupId, FirstParam, Duration");
                foreach (var row in logs)
                {
                    // Sanitize the first parameter for CSV usage. 
                    string val = row.FirstParam;
                    val = val.Replace('\r', ' ').Replace('\n', ' ').Replace(',', ';');

                    tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}",
                        row.Name,
                        row.Id,
                        row.ParentId,
                        row.GroupId,
                        val,
                        row.Duration);
                }

                var content = tw.ToString();

                
                var httpContent = new StringContent(content, System.Text.Encoding.UTF8, @"text/csv");
                var resp = new HttpResponseMessage { Content = httpContent };
                return resp;
            }
        }        
    }  
}