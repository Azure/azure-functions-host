using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using TriggerService;

namespace TriggerServiceRole.Controllers
{
    public class TriggerController : ApiController
    {
        // POST api/values
        public void Post(string callback)
        {
            IFrontEndSharedState state = new FrontEnd();

            // scope
            // Trigger[] triggers

            HttpClient c = new HttpClient();
            HttpResponseMessage rsp = c.GetAsync(callback).Result;

            var payload = rsp.Content.ReadAsAsync<AddTriggerPayload>().Result;


            try
            {
                payload.Validate();
            }
            catch (Exception e)
            {
                throw new HttpException(400, e.Message);
            }

            // !!! This just queues the request. How do we return a failure if we didn't add it properly?
            state.QueueAddTriggerRequest(callback, payload);

            // !!! Who validated the triggers?
        }     
    }
}