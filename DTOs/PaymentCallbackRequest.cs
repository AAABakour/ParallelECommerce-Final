namespace ParallelECommerce.DTOs;

public class PaymentCallbackRequest
{
    public string PaymentReference { get; set; } = "payment-req07-001";

    public string CallbackId { get; set; } = "callback-1";

    public decimal Amount { get; set; } = 99.99m;
}
