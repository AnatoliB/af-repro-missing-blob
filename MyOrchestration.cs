using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Company.Function
{
    public static class MyOrchestration
    {
        [Function(nameof(MyOrchestration))]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var isFirstGeneration = !context.GetInput<bool>();

            ILogger logger = context.CreateReplaySafeLogger(nameof(MyOrchestration));
            logger.LogInformation("RunOrchestrator started with isFirstGeneration = {isFirstGeneration}.", isFirstGeneration);

            await context.CallActivityAsync<string>(nameof(ActivityThatReturnsLargeOutput));

            var nextTime = context.CurrentUtcDateTime.AddSeconds(10);
            await context.CreateTimer(nextTime, CancellationToken.None);

            context.ContinueAsNew(true);
        }

        [Function(nameof(ActivityThatReturnsLargeOutput))]
        public static string ActivityThatReturnsLargeOutput([ActivityTrigger] string name, FunctionContext executionContext)
        {
            return new string('A', 65000);
        }

        [Function("MyOrchestration_HttpStart")]
        public static async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            ILogger logger = executionContext.GetLogger("MyOrchestration_HttpStart");

            // Function input comes from the request content.
            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(MyOrchestration));

            logger.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);

            // Returns an HTTP 202 response with an instance management payload.
            // See https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-http-api#start-orchestration
            return await client.CreateCheckStatusResponseAsync(req, instanceId);
        }
    }
}
