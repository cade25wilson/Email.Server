namespace Email.Server.DTOs.Responses.Billing;

public class CheckoutSessionResponse
{
    public required string SessionId { get; set; }
    public required string CheckoutUrl { get; set; }
}
