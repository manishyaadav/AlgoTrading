using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace May6StreamAnalytics
{
    public static class TimerOrchestationFunction
    {
        [FunctionName("AggregationOrchestrator1")]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            var aggregationRequest = context.GetInput<List<AggregationRequestItem>>();
            var firstItem = aggregationRequest.First();

            DateTime nextExecutionTime = GetNextExutionTime(log, firstItem.ProducerFrequencyInMins);
            Task timerTask = null;

            while (true) // Infinite loop
            {
                if (!context.IsReplaying)
                {
                    timerTask = context.CreateTimer(nextExecutionTime, CancellationToken.None);
                }

                var tasks = new List<Task<string>>();
                foreach (var item in aggregationRequest)
                {
                    tasks.Add(context.CallActivityAsync<string>(
                            "AggregationActivity",
                            (item.ExchangeName, item.PartialInstrumentName)));
                }

                // Handle both replay and non-replay cases
                var taskToAwait = timerTask ?? Task.Delay(Timeout.Infinite);

                await Task.WhenAny(taskToAwait, context.WaitForExternalEvent<bool>("Continue"));

                //// Check if orchestration should continue
                //if (!context.IsReplaying && context.GetInput<bool>("Continue") == false)
                //{
                //    break; // Terminate if 'Continue' event is false
                //}

                await Task.WhenAll(tasks);
                // Process aggregation results 

                // Recalculate for the next execution
                nextExecutionTime = GetNextExutionTime(log, firstItem.ProducerFrequencyInMins);
            }
        }

        [FunctionName("AggregationActivity")]
        public static async Task<string> RunAggregation([ActivityTrigger] (string exchangeName, string partialInstrumentName) input, ILogger log)
        {
            log.LogInformation($"Inside Activity for Exchange: {input.exchangeName} and Instrument: {input.partialInstrumentName}");
            // 1. Read data from Kafka (based on input, start time, end time)
            // 2. Perform aggregation
            return "done";
        }

        private static DateTime GetNextExutionTime (ILogger log, int frequency)
        {
            DateTime now = DateTime.Now;
            log.LogInformation($"Current Time: {now.ToString("MM/dd/yyyy hh:mm:ss tt")}");

            // Calculate seconds into the current minute
            int secondsIntoMinute = now.Second;
            log.LogInformation($"secondsIntoMinutes: {secondsIntoMinute}");

            // Calculate whole minutes to wait 
            int minutesToWait = (frequency - (now.Minute % frequency)) % frequency;
            minutesToWait = minutesToWait < 0 ? 0 : minutesToWait - 1;
            log.LogInformation($"Minutes to Wait: {minutesToWait}");

            // Calculate seconds to wait until the next 5-minute mark
            int secondsToWait = (minutesToWait * 60) + (60 - secondsIntoMinute);
            log.LogInformation($"secondsToWait: {secondsToWait}");
            //var initialDelay = TimeSpan.FromSeconds(secondsToWait);
            var nextExecutionTime = now.AddSeconds(secondsToWait);
            log.LogInformation($"next Execution Time: {nextExecutionTime}");
            return nextExecutionTime;
        }

        [FunctionName("StartAggregationWorkflow")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            try
            {
                string requestBody = await req.Content.ReadAsStringAsync(); // Use req.Content
                var aggregationRequest = JsonConvert.DeserializeObject<List<AggregationRequestItem>>(requestBody);

                // Display the data in the logs
                log.LogInformation("Received Aggregation Request:");
                foreach (var item in aggregationRequest.ToList())
                {
                    log.LogInformation($"Exchange Name: {item.ExchangeName}, Partial Instrument Name: {item.PartialInstrumentName}, Producer Frequency: {item.ProducerFrequencyInMins}");
                }

                // Start the orchestration (assuming you have an orchestrator)
                string instanceId = await starter.StartNewAsync("AggregationOrchestrator1", aggregationRequest);

                log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

                return starter.CreateCheckStatusResponse(req, instanceId);
            }
            catch (JsonException ex)
            {
                log.LogError("Deserialization error: " + ex.Message); // Log the specific error
                                                                      // Handle deserialization failure here

                return null;
            }
        }

        [FunctionName("StopAggregation")]
        public static async Task HttpStop(
           [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stop/{instanceId}")] HttpRequestMessage req,
           string instanceId, // Instance ID from the route
           [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            await client.RaiseEventAsync(instanceId, "Continue", false);
        }
    }
}