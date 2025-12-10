using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class BatchEmailResponse
{
    [JsonPropertyName("batch_id")]
    public Guid BatchId { get; set; } = Guid.NewGuid();

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("succeeded")]
    public int Succeeded { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("results")]
    public List<BatchEmailItemResult> Results { get; set; } = new();
}

public class BatchEmailItemResult
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message_id")]
    public Guid? MessageId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
