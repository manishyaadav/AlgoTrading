using Microsoft.AspNetCore.SignalR;

namespace SignalRServer.Hubs
{
    public class PivotMarkingHub : Hub
    {
        private readonly ILogger<PivotMarkingHub> _logger;

        public PivotMarkingHub(ILogger<PivotMarkingHub> logger)
        {
            _logger = logger;
        }

        public async Task SendMessage(string user, string message)
        {
            _logger.LogInformation($"Received message from {user}: {message} : Received At: {DateTime.Now}");
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}
