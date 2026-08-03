using Microsoft.AspNetCore.SignalR;

public class DataIngestionHub : Hub
{
private readonly ILogger<DataIngestionHub> _logger;

    public DataIngestionHub(ILogger<DataIngestionHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}