using System.Text.Json.Serialization;
using Dapr.Client;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Override any launchSettings.json nonsense
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") != null)
{
    builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
}

var app = builder.Build();

// Health endpoint
app.MapGet("/health", () => "OK");

// Shared Collections for Tracking Messages 
var allSentOrderIds = new ConcurrentBag<int>();

_ = Task.Run(async () =>
{
    await Task.Delay(2000);

    var messageCount = int.Parse(Environment.GetEnvironmentVariable("MESSAGE_COUNT") ?? "10");
    var maxConcurrentPublishes = int.Parse(Environment.GetEnvironmentVariable("MAX_CONCURRENT_PUBLISHES") ?? "5"); 
    var semaphore = new SemaphoreSlim(maxConcurrentPublishes);

    Console.WriteLine($"--- Publisher: Starting to publish {messageCount} messages with max concurrency of {maxConcurrentPublishes} ---");

    using var client = new DaprClientBuilder().Build();
    var publishTasks = new List<Task>();

    for (int i = 1; i <= messageCount; i++)
    {
        var order = new Order(i);
        allSentOrderIds.Add(order.OrderId);

        await semaphore.WaitAsync(); 

        publishTasks.Add(client.PublishEventAsync("orderpubsub", "orders", order)
            .ContinueWith(t =>
            {
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
});

await app.RunAsync();

public record Order([property: JsonPropertyName("orderId")] int OrderId);