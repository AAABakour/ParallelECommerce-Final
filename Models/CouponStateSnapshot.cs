namespace ParallelECommerce.Models;

public class CouponStateSnapshot
{
    public string Code { get; set; } = string.Empty;
    public int RemainingRedemptions { get; set; }
    public int TotalSuccessfulRedemptions { get; set; }
}
