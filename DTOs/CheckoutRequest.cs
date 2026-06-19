namespace ParallelECommerce.DTOs;

public class CheckoutRequest
{
    public int ProductId { get; set; } = 1;

    public int Quantity { get; set; } = 1;

    public string CustomerEmail { get; set; } = "customer@example.com";

    public string PaymentReference { get; set; } = "payment-req08-001";

    public decimal Amount { get; set; } = 1750m;

    public bool SimulateFailureAfterPayment { get; set; }
}
