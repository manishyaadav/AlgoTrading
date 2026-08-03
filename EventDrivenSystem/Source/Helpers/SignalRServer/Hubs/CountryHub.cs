using Microsoft.AspNetCore.SignalR;

public class CountryHub : Hub
{
private readonly ILogger<CountryHub> _logger;

    public CountryHub(ILogger<CountryHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}