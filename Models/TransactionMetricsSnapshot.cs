namespace ParallelECommerce.Models;

public class TransactionMetricsSnapshot
{
    public string ProviderName { get; set; } = string.Empty;

    public int InitialStock { get; set; }

    public int CurrentStock { get; set; }

    public int OrdersCreated { get; set; }

    public int CapturedPayments { get; set; }

    public int InconsistentPartialOperations { get; set; }

    public long BeforeAttempts { get; set; }

    public long BeforePaymentsCaptured { get; set; }

    public long BeforeInventoryDeductions { get; set; }

    public long BeforeOrdersCreated { get; set; }

    public long BeforePartialFailures { get; set; }

    public long AfterAttempts { get; set; }

    public long AfterCommits { get; set; }

    public long AfterRollbacks { get; set; }

    public long AfterPaymentsCaptured { get; set; }

    public long AfterInventoryDeductions { get; set; }

    public long AfterOrdersCreated { get; set; }

    public long AfterOutOfStockFailures { get; set; }

    public DateTime LastResetAtUtc { get; set; }

    public string Explanation { get; set; } = string.Empty;
}
