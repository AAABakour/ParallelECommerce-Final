namespace ParallelECommerce.DTOs;

public class CouponRedemptionRequest
{
    public string UserId { get; set; } = "user-1";

    public string CouponCode { get; set; } = "FLASH-100";
}
