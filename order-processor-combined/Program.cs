using System.Text.Json.Serialization;
using Dapr;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddDapr();

var app = builder.Build();

app.UseRouting();
app.UseCloudEvents();
app.UseEndpoints(endpoints =>
{
    endpoints.MapSubscribeHandler();
    endpoints.MapControllers();
});

// Health endpoint
app.MapGet("/health", () => "OK");

// Shared Collections for Tracking Messages 
var allSentOrderIds = new ConcurrentBag<int>();
OrderController.AllReceivedOrderIds = new ConcurrentBag<int>(); 

_ = Task.Run(async () =>
{
    await Task.Delay(2000);

    var messageCount = int.Parse(Environment.GetEnvironmentVariable("MESSAGE_COUNT") ?? "10");
    // --- CONCURRENCY CONTROL: Define max concurrent tasks ---
    var maxConcurrentPublishes = int.Parse(Environment.GetEnvironmentVariable("MAX_CONCURRENT_PUBLISHES") ?? "5"); 
    var semaphore = new SemaphoreSlim(maxConcurrentPublishes);

    Console.WriteLine($"--- Publisher: Starting to publish {messageCount} messages with max concurrency of {maxConcurrentPublishes} ---");

    using var client = new DaprClientBuilder().Build();

    // List to hold all the publishing tasks
    var publishTasks = new List<Task>();

    for (int i = 1; i <= messageCount; i++)
    {
        var order = new Order(i);
        allSentOrderIds.Add(order.OrderId);

        // Acquire a semaphore slot before starting the publish task
        await semaphore.WaitAsync(); 

        publishTasks.Add(client.PublishEventAsync("orderpubsub", "orders", order)
            .ContinueWith(t =>
            {
                // Release the semaphore slot once the task completes (regardless of success/failure)
                semaphore.Release(); 

                if (t.IsFaulted)
                {
                    Console.Error.WriteLine($"--- PUBLISH FAILED for {order}: {t.Exception?.InnerException?.Message ?? t.Exception?.Message} ---");
                }
                else if (t.IsCanceled)
                {
                    Console.Error.WriteLine($"--- PUBLISH CANCELED for {order}. ---");
                }
                else
                {
                    Console.WriteLine($"Published: {order}");
                }
            }));
    }

    await Task.WhenAll(publishTasks);

    Console.WriteLine($"--- Publisher: Finished attempting to publish {messageCount} messages. ---");

    // --- Dynamic Wait for Subscriber to Catch Up ---
    Console.WriteLine("--- Waiting for subscriber to catch up (dynamic wait)... ---");
    int previousReceivedCount = 0;
    int consecutiveSameCount = 0;
    const int maxConsecutiveSameCount = 5;
    const int checkIntervalMs = 2000;

    while (OrderController.AllReceivedOrderIds.Count < messageCount)
    {
        await Task.Delay(checkIntervalMs);
        int currentReceivedCount = OrderController.AllReceivedOrderIds.Count;

        if (currentReceivedCount == previousReceivedCount)
        {
            consecutiveSameCount++;
            Console.WriteLine($"--- Subscriber count stagnant ({currentReceivedCount} received). Consecutive checks: {consecutiveSameCount}/{maxConsecutiveSameCount} ---");
            if (consecutiveSameCount >= maxConsecutiveSameCount)
            {
                Console.WriteLine("--- Subscriber count has been stagnant for too long. Assuming no more messages are coming. ---");
                break; 
            }
        }
        else
        {
            consecutiveSameCount = 0; 
            Console.WriteLine($"--- Subscriber received count: {currentReceivedCount}/{messageCount} ---");
        }
        previousReceivedCount = currentReceivedCount;
    }
    

    // --- Generate Report ---
    Console.WriteLine("\n--- MESSAGE DELIVERY REPORT ---");

    var distinctSentCount = allSentOrderIds.Distinct().Count();
    var distinctReceivedCount = OrderController.AllReceivedOrderIds.Distinct().Count();

    Console.WriteLine($"Total messages attempted to publish: {distinctSentCount}");
    Console.WriteLine($"Total messages successfully received: {distinctReceivedCount}");

    if (distinctSentCount != distinctReceivedCount)
    {
        var missingOrderIds = allSentOrderIds.Except(OrderController.AllReceivedOrderIds).OrderBy(id => id).ToList();
        Console.Error.WriteLine($"\n!!! MESSAGE LOSS DETECTED !!!");
        Console.Error.WriteLine($"Number of lost messages: {missingOrderIds.Count}");
        Console.Error.WriteLine($"Lost Order IDs: [{string.Join(", ", missingOrderIds)}]");
    }
    else
    {
        Console.WriteLine("\nAll messages accounted for. No message loss detected.");
    }
    Console.WriteLine("-------------------------------\n");
});

await app.RunAsync();

[ApiController]
public class OrderController : ControllerBase
{
    public static ConcurrentBag<int> AllReceivedOrderIds = new();
    
    [HttpPost("orders")]
    public ActionResult<Order> HandleOrder(Order order)
    {
        var failRate = double.Parse(Environment.GetEnvironmentVariable("SUBSCRIBER_FAIL_RATE") ?? "0.0");
        var messagesToFailCount = (int)(int.Parse(Environment.GetEnvironmentVariable("MESSAGE_COUNT") ?? "10") * failRate);

        if (order.OrderId <= messagesToFailCount)
        {
            Console.Error.WriteLine($"Subscriber FAILED to process: {order} (deterministic failure for DLQ)");
            return StatusCode(500);
        }

        AllReceivedOrderIds.Add(order.OrderId);
        Console.WriteLine($"Subscriber received: {order} (SUCCESS)");
        return Ok(order);
    }
}

public record Order([property: JsonPropertyName("orderId")] int OrderId);