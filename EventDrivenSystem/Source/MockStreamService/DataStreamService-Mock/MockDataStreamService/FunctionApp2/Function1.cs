//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Net.Http;
//using System.Threading;
//using System.Threading.Tasks;
//using May6StreamAnalytics;
//using Microsoft.AspNetCore.Http;
//using Microsoft.Azure.WebJobs;
//using Microsoft.Azure.WebJobs.Extensions.DurableTask;
//using Microsoft.Azure.WebJobs.Extensions.Http;
//using Microsoft.Azure.WebJobs.Host;
//using Microsoft.Extensions.Logging;
//using Newtonsoft.Json;
//using static Microsoft.AspNetCore.Hosting.Internal.HostingApplication;

//namespace FunctionApp2
//{
//    public static class Function1
//    {
       

//        [FunctionName("AggregationOrchestrator")]
//            public static async Task RunOrchestrator(
//                [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
//                {
//                    var aggregationRequest = context.GetInput<List<AggregationRequestItem>>();

//                    var tasks = new List<Task<string>>(); // To store aggregation results
//                    foreach (var item in aggregationRequest.ToList())
//                    {
//                        tasks.Add(context.CallActivityAsync<string>(
//                            "AggregationActivity",
//                            (item.ExchangeName, item.PartialInstrumentName)));


//                        // Get the current time
//                        DateTime now = DateTime.Now;
//                        log.LogInformation($"Current Time: {now.ToString("MM/dd/yyyy hh:mm:ss tt")}");

//                        // Calculate seconds into the current minute
//                        int secondsIntoMinute = now.Second;
//                        log.LogInformation($"secondsIntoMinutes: {secondsIntoMinute}");

//                        // Calculate whole minutes to wait 
//                        int minutesToWait = (item.ProducerFrequencyInMins - (now.Minute % item.ProducerFrequencyInMins)) % item.ProducerFrequencyInMins;
//                        minutesToWait = minutesToWait < 0 ? 1 : minutesToWait - 1;
//                        log.LogInformation($"Minutes to Wait: {minutesToWait}");

//                        // Calculate seconds to wait until the next 5-minute mark
//                        int secondsToWait = (minutesToWait * 60) + (60 - secondsIntoMinute);
//                        log.LogInformation($"secondsToWait: {secondsToWait}");
//                        var initialDelay = TimeSpan.FromSeconds(secondsToWait);
//                        await context.CreateTimer(now.Add(initialDelay), CancellationToken.None);
//                    }

//                    await Task.WhenAll(tasks);
//                    // Process aggregation results 
//                }


        

//        [FunctionName("AggregationActivity")]
//        public static async Task<string> RunAggregation([ActivityTrigger] (string exchangeName, string partialInstrumentName) input, ILogger log)
//        {
//            log.LogInformation($"Inside Activity for Exchange: {input.exchangeName} and Instrument: {input.partialInstrumentName}");
//            // 1. Read data from Kafka (based on input, start time, end time)
//            // 2. Perform aggregation
//            return "done";
//        }

        

//        [FunctionName("YourStarterFunction")]
//        public static async Task<HttpResponseMessage> HttpStart1(
//            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestMessage req,
//            [DurableClient] IDurableOrchestrationClient starter,
//            ILogger log)
//        {
//            // Read and deserialize the request body
//            //string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
//            //AggregationRequest aggregationRequest = JsonConvert.DeserializeObject<AggregationRequest>(requestBody);

//            try
//            {
//                string requestBody = await req.Content.ReadAsStringAsync(); // Use req.Content
//                var aggregationRequest = JsonConvert.DeserializeObject<List<AggregationRequestItem>>(requestBody);

//                // Display the data in the logs
//                log.LogInformation("Received Aggregation Request:");
//                foreach (var item in aggregationRequest.ToList())
//                {
//                    log.LogInformation($"Exchange Name: {item.ExchangeName}, Partial Instrument Name: {item.PartialInstrumentName}, Producer Frequency: {item.ProducerFrequencyInMins}");
//                }

//                // Start the orchestration (assuming you have an orchestrator)
//                string instanceId = await starter.StartNewAsync("AggregationOrchestrator", aggregationRequest);

//                log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

//                return starter.CreateCheckStatusResponse(req, instanceId);
//            }
//            catch (JsonException ex)
//            {
//                log.LogError("Deserialization error: " + ex.Message); // Log the specific error
//                                                                      // Handle deserialization failure here

//                return null;
//            }



//        }
//    }
//}