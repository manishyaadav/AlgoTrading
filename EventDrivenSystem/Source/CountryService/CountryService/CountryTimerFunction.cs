using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SharedLibrary.Enums;
using SharedLibrary.Events;
using SharedLibrary.Helpers;
using SharedLibrary.Models;

namespace CountryService
{
    public class CountryTimerFunction
    {
        private readonly ILogger _logger;
        private readonly IProducer<string, string> _producer;
        private static string _producerTopicName = string.Empty;
        private static string _environmentName  = string.Empty;
        private static readonly HttpClient httpClient = new HttpClient();

        public CountryTimerFunction(ILoggerFactory loggerFactory, IProducer<string, string> producer)
        {
            _logger = loggerFactory.CreateLogger<CountryTimerFunction>();
            _producer = producer;
            _producerTopicName = Environment.GetEnvironmentVariable("ProducerTopicName") ?? "";
            _environmentName = Environment.GetEnvironmentVariable("EnvironmentName") ?? "";
        }

        [Function("CountryTimerFunction")]
        //public async Task Run([TimerTrigger("0 30 23 * * *")] TimerInfo myTimer)
        public async Task Run([TimerTrigger("0 1 0 * * *")] TimerInfo myTimer)
        {
            try
            {
                DateTime utcDate = DateTime.UtcNow;
                _logger.LogInformation($"CountryTimerFunction function executed at: {utcDate.ToLocalTime().ToString("yyyy-mm-dd")}");
                
                if (myTimer.ScheduleStatus is not null)
                {
                    _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next.ToLocalTime}");
                }

                // Get Holiday from Blob

                CountryEvent countryEvent = await CreateCountryEvent(utcDate, _logger);                
                await ProduceToKafka(_producerTopicName, countryEvent,  _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ERROR: Country Timer failed: {ex.Message}");
            }
            
            
        }

        public async Task<CountryEvent> CreateCountryEvent(DateTime date, ILogger _logger)
        {
            var dayOfWeek = date.DayOfWeek;           

            var holidays = await GetHolidays(date, _logger);
            var first = holidays.FirstOrDefault();
            var next = holidays.Skip(1).FirstOrDefault();

            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(date);

            var isTodayHoliday = first?.Date.Date == date.Date;
            string holidayDate = string.Empty;
            string holidayReason = string.Empty;

            if (first != null)
            {
                holidayDate = DateTimeHelper.ToIsoStringWithoutTime(DateTimeHelper.ConvertToDateTimeOffset(first.Date));
                holidayReason = first.Reason;
            }
                
            CountryEvent countryEvent = new CountryEvent() { 
                Date = DateTimeHelper.ToIsoStringWithoutTime(indianDateTimeOffset),
                Holiday = isTodayHoliday ? new HolidayItem() { Date = holidayDate, Reason = holidayReason } : null,
                NextHoliday = next != null ? (isTodayHoliday ? new HolidayItem() { Date = DateTimeHelper.ToIsoStringWithoutTime(DateTimeHelper.ConvertToDateTimeOffset(next.Date)), Reason = next.Reason} : new HolidayItem() { Date = holidayDate, Reason = holidayReason }) : new HolidayItem(),
                State = isTodayHoliday ? CountryStateEnum.Holiday : (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday) ? CountryStateEnum.Weekend : CountryStateEnum.Normal,
                StateName = isTodayHoliday ? CountryStateEnum.Holiday.ToString() : (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday) ? CountryStateEnum.Weekend.ToString() : CountryStateEnum.Normal.ToString(),
                Producer = "country.service",
                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
            };
            return countryEvent;
        }

        public async Task<List<HolidayDTO>> GetHolidays(DateTime date, ILogger _logger)
        {
            try
            {
                int year = date.Year;
                // Making the GET request to the API
                string apiDomain = Environment.GetEnvironmentVariable("HOLIDAY_API") ?? "";
                _logger.LogInformation($"HOLIDAY_API: {apiDomain}");
                var fullUrl = $"{apiDomain}/api/GetHolidays?blobName=master&year={date.Year.ToString()}";
                _logger.LogInformation($"FULL URL: {fullUrl}");
                var response = await httpClient.GetAsync(fullUrl);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"API request failed with status code: {response.StatusCode}");
                    return new List<HolidayDTO>();
                }

                // Assuming the response is in JSON format
                var content = await response.Content.ReadAsStringAsync();
               
                var holidayResponse = JsonSerializer.Deserialize<List<HolidayDTO>>(content);
                if (holidayResponse != null)
                {
                    _logger.LogWarning($"number of holidays: {holidayResponse.Count}");

                    var filteredHolidays = holidayResponse
                        .Where(x => x.Date.Date >= date.Date)
                        .OrderBy(x => x.Date)
                        .Take(2)
                        .ToList();


                    return filteredHolidays ?? new List<HolidayDTO>();
                }
                return new List<HolidayDTO>();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception occurred: {ex.Message}");
                return new List<HolidayDTO>();
            }
        }

        private async Task ProduceToKafka(string topicName, CountryEvent eventValue,  ILogger logger)
        {
            try
            {
                var key = $"{eventValue.Name}:{eventValue.Date:dd/MM/yyyy}";
                var kafkaMessage = JsonSerializer.Serialize(eventValue);
                // Create headers with required values
                var headers = new Headers
                {
                    { "country", Encoding.UTF8.GetBytes(eventValue.Name) },
                     { "date", Encoding.UTF8.GetBytes(eventValue.Date) },
                    { "environment", Encoding.UTF8.GetBytes(_environmentName) }
                };


                var deliveryReport = await _producer.ProduceAsync
                        (
                            topicName, 
                            new Message<string, string> 
                            {
                                Key = key,
                                Value = kafkaMessage,
                                Headers = headers
                            }
                        );
                logger.LogInformation($"Delivered message to: {deliveryReport.TopicPartitionOffset}");
            }
            catch (ProduceException<string, string> e)
            {
                logger.LogError($"Delivery failed: {e.Error.Reason}");
            }
        }
        
    }
}

