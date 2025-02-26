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
            var generation = context.GetInput<int>();

            ILogger logger = context.CreateReplaySafeLogger(nameof(MyOrchestration));
            logger.LogInformation("RunOrchestrator started (generation {generation}).", generation);

            if (generation == 0)
            {
                var tasks = new List<Task<string>>();
                for (int i = 0; i < 100; i++)
                {
                    tasks.Add(context.CallActivityAsync<string>(nameof(ActivityThatReturnsLargeOutput)));
                }

                await Task.WhenAll(tasks);

                context.ContinueAsNew(generation + 1);
            }
            else
            {
                context.SetCustomStatus("I'm in gen 2");
                await context.WaitForExternalEvent<bool>("BlockIndefinitely");
            }
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
