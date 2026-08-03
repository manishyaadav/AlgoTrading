using Microsoft.AspNetCore.SignalR;

public class ExchangeHub : Hub
{
private readonly ILogger<ExchangeHub> _logger;

    public ExchangeHub(ILogger<ExchangeHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}