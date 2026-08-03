using Microsoft.AspNetCore.SignalR;

public class StrategyHub : Hub
{
private readonly ILogger<StrategyHub> _logger;

    public StrategyHub(ILogger<StrategyHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}