using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Company.Function
{
    public static class MyOrchestration
    {
        private static readonly Random random = new(0);

        [Function(nameof(MyOrchestration))]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var generation = context.GetInput<int>();

            ILogger logger = context.CreateReplaySafeLogger(nameof(MyOrchestration));
            logger.LogInformation("RunOrchestrator started (generation {generation}).", generation);

            int activities = await context.CallActivityAsync<int>(nameof(GetRandom), 200);
            logger.LogInformation("Generation {generation} will have {activities} activities.", generation, activities);

            var delay = TimeSpan.FromSeconds(10);
            await context.CreateTimer(context.CurrentUtcDateTime.Add(delay), CancellationToken.None);

            for (int i = 0; i < activities; i++)
            {
                await context.CallActivityAsync<string>(nameof(ActivityThatReturnsLargeOutput));
            }

            logger.LogInformation("Finishing generation {generation} ({activities} activities). ContinueAsNew in {delay}...", generation, activities, delay);
            await context.CreateTimer(context.CurrentUtcDateTime.Add(delay), CancellationToken.None);
            context.ContinueAsNew(generation + 1);
        }

        [Function(nameof(ActivityThatReturnsLargeOutput))]
        public static string ActivityThatReturnsLargeOutput([ActivityTrigger] string name, FunctionContext executionContext)
        {
            return new string('A', 65000);
        }

        [Function(nameof(GetRandom))]
        public static int GetRandom([ActivityTrigger] int max)
        {
            return random.Next(max);
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
