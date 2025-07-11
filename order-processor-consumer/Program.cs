using System.Text.Json.Serialization;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddDapr();
builder.Services.AddHttpLogging(options =>
   {
       // Configure options here if needed. For example:
       // options.LoggingFields = HttpLoggingFields.RequestHeaders | HttpLoggingFields.ResponseHeaders;
   });

var app = builder.Build();

app.MapGet("/health", () => "OK");

app.UseRouting();
app.UseCloudEvents();
app.UseHttpLogging();
app.UseEndpoints(endpoints =>
{
    endpoints.MapSubscribeHandler();
    endpoints.MapControllers();
});

await app.RunAsync();

[ApiController]
public class OrderController : ControllerBase
{
    private static readonly ConcurrentBag<int> allReceivedOrderIds = new();
    
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

        allReceivedOrderIds.Add(order.OrderId);
        Console.WriteLine($"Subscriber received: {order} (SUCCESS)");
        return Ok(order);
    }
}

public record Order([property: JsonPropertyName("orderId")] int OrderId);