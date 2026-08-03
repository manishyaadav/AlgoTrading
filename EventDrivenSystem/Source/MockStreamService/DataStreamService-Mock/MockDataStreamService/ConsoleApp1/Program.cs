using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        GetNextTrigger(10);
    }

    private static void GetNextTrigger(int min)
    {
        // Get the current time
        DateTime now = DateTime.Now;
        Console.WriteLine($"Current Time: {now.ToString("MM/dd/yyyy hh:mm:ss tt")}");

        // Calculate seconds into the current minute
        int secondsIntoMinute = now.Second;
        Console.WriteLine($"secondsIntoMinutes: {secondsIntoMinute}");

        // Calculate whole minutes to wait 
        int minutesToWait = (min - (now.Minute % min)) % min;
        minutesToWait = minutesToWait < 0 ? 1 : minutesToWait-1;
        Console.WriteLine($"Minutes to Wait: {minutesToWait}");

        // Calculate seconds to wait until the next 5-minute mark
        int secondsToWait = (minutesToWait * 60) + (60 - secondsIntoMinute);
        Console.WriteLine($"secondsToWait: {secondsToWait}");
        
    }
}