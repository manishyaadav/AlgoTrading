using Microsoft.AspNetCore.SignalR;

public class IndicatorHub : Hub
{
private readonly ILogger<IndicatorHub> _logger;

    public IndicatorHub(ILogger<IndicatorHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}