using Microsoft.AspNetCore.SignalR;

public class PortfolioHub : Hub
{
private readonly ILogger<PortfolioHub> _logger;

    public PortfolioHub(ILogger<PortfolioHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}