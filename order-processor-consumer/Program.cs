using System.Text.Json.Serialization;
using Dapr;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Override any launchSettings.json nonsense
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") != null)
{
    builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
}

var app = builder.Build();

app.UseCloudEvents();

// Health endpoint
app.MapGet("/health", () => "OK");

// Shared Collections for Tracking Messages 
var allReceivedOrderIds = new ConcurrentBag<int>(); 

// Message handler
app.MapPost("/orders", (Order order) => {
    var failRate = double.Parse(Environment.GetEnvironmentVariable("SUBSCRIBER_FAIL_RATE") ?? "0.0");
    var messagesToFailCount = (int)(int.Parse(Environment.GetEnvironmentVariable("MESSAGE_COUNT") ?? "10") * failRate);

    if (order.OrderId <= messagesToFailCount)
    {
        Console.Error.WriteLine($"Subscriber FAILED to process: {order} (deterministic failure for DLQ)");
        return Results.StatusCode(500);
    }

    allReceivedOrderIds.Add(order.OrderId);
    Console.WriteLine($"Subscriber received: {order} (SUCCESS)");
    return Results.Ok(order);
});

// Optional: Periodic stats reporting
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(10000); // Report every 10 seconds
        var count = allReceivedOrderIds.Count;
        if (count > 0)
        {
            Console.WriteLine($"--- Consumer stats: {count} messages received so far ---");
        }
    }
});

await app.RunAsync();

public record Order([property: JsonPropertyName("orderId")] int OrderId);