using Microsoft.AspNetCore.SignalR;

public class DataFeedHub : Hub
{
private readonly ILogger<DataFeedHub> _logger;

    public DataFeedHub(ILogger<DataFeedHub> logger)
    {
        _logger = logger;
    }

    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}