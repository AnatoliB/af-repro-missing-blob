using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Company.Function
{
    public static class MyOrchestration
    {
        private static readonly string funkyActivityFilePath = Path.GetTempPath() + "proof_I_ran_before.txt";

        [Function(nameof(MyOrchestration))]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var generation = context.GetInput<int>();

            ILogger logger = context.CreateReplaySafeLogger(nameof(MyOrchestration));
            logger.LogInformation("RunOrchestrator started (generation {generation}).", generation);

            if (generation == 0)
            {
                for (int i = 0; i < 100; i++)
                {
                    await context.CallActivityAsync<string>(nameof(ActivityThatReturnsLargeOutput));
                }

                var funkyTask = context.CallActivityAsync<string>(nameof(FunkyActivity));
                var timer = context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(10), CancellationToken.None);
                context.SetCustomStatus("Before Task.WhenAny");
                logger.LogWarning("Before Task.WhenAny");
                await Task.WhenAny(funkyTask, timer);
                logger.LogWarning("After Task.WhenAny");
                context.SetCustomStatus("After Task.WhenAny");

                context.ContinueAsNew(generation + 1);
                logger.LogWarning("After ContinueAsNew");
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

        [Function(nameof(FunkyActivity))]
        public static async Task<string> FunkyActivity([ActivityTrigger] string name, FunctionContext executionContext)
        {
            if (File.Exists(funkyActivityFilePath))
            {
                return "I ran before!";
            }

            File.WriteAllText(funkyActivityFilePath, "I ran before");
            
            await Task.Delay(Timeout.Infinite);
            return "I've been waiting forever...";
        }

        [Function("MyOrchestration_HttpStart")]
        public static async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            ILogger logger = executionContext.GetLogger("MyOrchestration_HttpStart");

            if (File.Exists(funkyActivityFilePath))
            {
                File.Delete(funkyActivityFilePath);
            }

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
