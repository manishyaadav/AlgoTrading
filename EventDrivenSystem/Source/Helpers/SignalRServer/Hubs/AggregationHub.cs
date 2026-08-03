using Microsoft.AspNetCore.SignalR;

public class AggregationHub : Hub
{
private readonly ILogger<AggregationHub> _logger;

    public AggregationHub(ILogger<AggregationHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}