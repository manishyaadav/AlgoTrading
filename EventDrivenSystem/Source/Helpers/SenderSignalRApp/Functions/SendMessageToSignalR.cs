using System;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SenderSignalRApp
{
    public class SendMessageToSignalR
    {
        private readonly ILogger _logger;
        private readonly string _signalRServiceUrl;

        public SendMessageToSignalR(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<SendMessageToSignalR>();
            _signalRServiceUrl = configuration["SignalRServiceUrl"] ?? "";
            _logger.LogInformation($"SignalR Server URL: {_signalRServiceUrl}");
        }

        [Function("SendMessageToSignalR")]
        public async Task Run([TimerTrigger("0 * * * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            
            NotificationMessage message = new NotificationMessage()
            {
                User = "TimerFunction",
                Message = $"Sending message at: {DateTime.Now}"
            };
            
            var connection = new HubConnectionBuilder()
            .WithUrl(_signalRServiceUrl)
            .Build();
            
            await connection.StartAsync();
            await connection.InvokeAsync("SendMessage", message.User, message.Message);

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next.ToLocalTime().ToString()}");
            }
        }
    }

    public class NotificationMessage
    {
        public string User { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
