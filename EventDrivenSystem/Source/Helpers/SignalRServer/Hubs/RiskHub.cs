using Microsoft.AspNetCore.SignalR;

public class RiskHub : Hub
{
private readonly ILogger<RiskHub> _logger;

    public RiskHub(ILogger<RiskHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}