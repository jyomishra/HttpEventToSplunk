using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MyHTTPTrigger
{
    public static class HTTPTriggerSendToSplunk
    {
        [FunctionName("HTTPTriggerSendToSplunk")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            List<string> listMessage = new List<string>();
            listMessage.Add(requestBody);
            await Utils.sendViaHEC(listMessage, log);

            return new OkObjectResult("Ok");
        }
    }
}
