using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class CreateDomainRequest
{
    [Required]
    [MaxLength(255)]
    [JsonPropertyName("domain")]
    public required string Domain { get; set; }

    [Required]
    [MaxLength(32)]
    [JsonPropertyName("region")]
    public required string Region { get; set; }
}
