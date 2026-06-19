using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class InventoryService
{
    private readonly Dictionary<int, Product> _products = new();

    // هذا القفل يحمي المخزون من Race Condition داخل نسخة After.
    private readonly object _stockLock = new();

    public InventoryService()
    {
        _products[1] = new Product
        {
            Id = 1,
            Name = "Gaming Laptop",
            Price = 1750,
            StockQuantity = 10,
            PopularityScore = 99
        };

        _products[2] = new Product
        {
            Id = 2,
            Name = "Mechanical Keyboard",
            Price = 120,
            StockQuantity = 80,
            PopularityScore = 91
        };

        _products[3] = new Product
        {
            Id = 3,
            Name = "Wireless Mouse",
            Price = 65,
            StockQuantity = 150,
            PopularityScore = 88
        };

        _products[4] = new Product
        {
            Id = 4,
            Name = "USB-C Docking Station",
            Price = 210,
            StockQuantity = 35,
            PopularityScore = 73
        };

        _products[5] = new Product
        {
            Id = 5,
            Name = "Noise Cancelling Headset",
            Price = 260,
            StockQuantity = 45,
            PopularityScore = 86
        };
    }

    public Product? GetProduct(int productId)
    {
        lock (_stockLock)
        {
            return _products.TryGetValue(productId, out var product)
                ? Clone(product)
                : null;
        }
    }

    public List<Product> GetAllProducts()
    {
        lock (_stockLock)
        {
            return _products.Values
                .Select(Clone)
                .OrderBy(product => product.Id)
                .ToList();
        }
    }

    public List<Product> GetPopularProducts(int count)
    {
        count = Math.Clamp(count, 1, 20);

        lock (_stockLock)
        {
            return _products.Values
                .OrderByDescending(product => product.PopularityScore)
                .ThenBy(product => product.Id)
                .Take(count)
                .Select(Clone)
                .ToList();
        }
    }

    public void ResetStock(int productId, int quantity)
    {
        lock (_stockLock)
        {
            if (_products.TryGetValue(productId, out var product))
            {
                product.StockQuantity = quantity;
            }
        }
    }

    // BEFORE: نسخة غير آمنة، فيها Race Condition.
    public async Task<bool> PurchaseBeforeAsync(int productId, int quantity)
    {
        Product? product;

        lock (_stockLock)
        {
            _products.TryGetValue(productId, out product);
        }

        if (product is null)
        {
            return false;
        }

        if (product.StockQuantity < quantity)
        {
            return false;
        }

        // تأخير مقصود حتى نكبر احتمال حدوث Race Condition.
        await Task.Delay(100);

        // لا نستخدم lock هنا عمداً حتى يبقى Endpoint قبل الحل قادراً على إثبات المشكلة.
        product.StockQuantity -= quantity;

        return true;
    }

    // AFTER: نسخة آمنة باستخدام lock.
    public bool PurchaseAfter(int productId, int quantity)
    {
        lock (_stockLock)
        {
            if (!_products.TryGetValue(productId, out var product))
            {
                return false;
            }

            if (product.StockQuantity < quantity)
            {
                return false;
            }

            product.StockQuantity -= quantity;

            return true;
        }
    }

    private static Product Clone(Product product)
    {
        return new Product
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            StockQuantity = product.StockQuantity,
            PopularityScore = product.PopularityScore
        };
    }
}
