namespace SharedLibrary
{
    // No [JsonPropertyName] renaming here on purpose — this service deserializes with
    // Newtonsoft.Json (same as every other consumer in this pipeline), which ignores
    // System.Text.Json's JsonPropertyNameAttribute and matches the plain C# member name
    // case-insensitively instead. See NotificationService/README.md's cross-service JSON contract
    // section for the bug class this avoids.
    public class CimplifyBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string TimeZoneId { get; set; } = "Asia/Kolkata";

        public string Version { get; set; } = "1.0";
    }
}
