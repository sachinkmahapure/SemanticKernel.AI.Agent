using System.ComponentModel;
using System.Text;
using System.Text.Json;
using AI.ChatAgent.Data;
using AI.ChatAgent.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AI.ChatAgent.Plugins;

/// <summary>
/// Semantic Kernel plugin that exposes Entity Framework Core queries
/// to the AI kernel. The AI can call these functions to retrieve live
/// data from the SQL database.
/// </summary>
public sealed class DatabasePlugin(
    ChatAgentDbContext db,
    ILogger<DatabasePlugin> logger)
{
    // ── Products ─────────────────────────────────────────────────────────────

    /// <summary>Search products by name, category, or description.</summary>
    [KernelFunction(nameof(SearchProducts))]
    [Description("Search products by name, category or keyword. Returns JSON array of matching products.")]
    public async Task<string> SearchProducts(
        [Description("Search keyword (name, category, or partial description)")] string query,
        [Description("Optional category filter")] string? category = null,
        [Description("Maximum number of results (default 20)")] int maxResults = 20,
        CancellationToken ct = default)
    {
        logger.LogInformation("DB:SearchProducts query={Query} category={Category}", query, category);

        var q = db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(p => EF.Functions.Like(p.Name, $"%{query}%") ||
                              EF.Functions.Like(p.Description ?? "", $"%{query}%"));

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(p => p.Category == category);

        var results = await q
            .OrderBy(p => p.Name)
            .Take(Math.Clamp(maxResults, 1, 100))
            .Select(p => new
            {
                p.Id, p.Name, p.Description, p.Price,
                p.Category, p.StockQuantity
            })
            .ToListAsync(ct);

        logger.LogInformation("DB:SearchProducts returned {Count} products", results.Count);
        return JsonSerializer.Serialize(results);
    }

    /// <summary>Get product details by ID.</summary>
    [KernelFunction(nameof(GetProductById))]
    [Description("Retrieve a single product by its numeric ID.")]
    public async Task<string> GetProductById(
        [Description("Product ID")] int productId,
        CancellationToken ct = default)
    {
        logger.LogInformation("DB:GetProductById id={Id}", productId);

        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, ct);

        if (product is null)
            return $"{{\"error\": \"Product {productId} not found\"}}";

        return JsonSerializer.Serialize(product);
    }

    // ── Customers ────────────────────────────────────────────────────────────

    /// <summary>Search customers by name or email.</summary>
    [KernelFunction(nameof(SearchCustomers))]
    [Description("Search customers by name or email address.")]
    public async Task<string> SearchCustomers(
        [Description("Customer name or email to search for")] string query,
        CancellationToken ct = default)
    {
        logger.LogInformation("DB:SearchCustomers query={Query}", query);

        var customers = await db.Customers.AsNoTracking()
            .Where(c =>
                EF.Functions.Like(c.FirstName, $"%{query}%") ||
                EF.Functions.Like(c.LastName,  $"%{query}%") ||
                EF.Functions.Like(c.Email,     $"%{query}%"))
            .Take(50)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(customers);
    }

    // ── Orders ───────────────────────────────────────────────────────────────

    /// <summary>Get orders filtered by status with optional customer filter.</summary>
    [KernelFunction(nameof(GetOrders))]
    [Description("Retrieve orders. Optionally filter by status (Pending/Confirmed/Shipped/Delivered/Cancelled) or customer ID.")]
    public async Task<string> GetOrders(
        [Description("Order status filter (optional)")] string? status = null,
        [Description("Customer ID filter (optional)")] int? customerId = null,
        [Description("Max results (default 25)")] int maxResults = 25,
        CancellationToken ct = default)
    {
        logger.LogInformation("DB:GetOrders status={Status} customerId={CustomerId}", status, customerId);

        var q = db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(o => o.Status == status);

        if (customerId.HasValue)
            q = q.Where(o => o.CustomerId == customerId.Value);

        var orders = await q
            .OrderByDescending(o => o.OrderDate)
            .Take(Math.Clamp(maxResults, 1, 100))
            .Select(o => new
            {
                o.Id,
                CustomerName = o.Customer != null ? $"{o.Customer.FirstName} {o.Customer.LastName}" : "Unknown",
                o.Status,
                o.TotalAmount,
                o.OrderDate,
                Items = o.Items.Select(i => new
                {
                    ProductName = i.Product != null ? i.Product.Name : "Unknown",
                    i.Quantity,
                    i.UnitPrice
                })
            })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(orders);
    }

    // ── Analytics ────────────────────────────────────────────────────────────

    /// <summary>Get business summary statistics.</summary>
    [KernelFunction(nameof(GetBusinessStats))]
    [Description("Return a business summary: total revenue, order counts, top products, customer count.")]
    public async Task<string> GetBusinessStats(CancellationToken ct = default)
    {
        logger.LogInformation("DB:GetBusinessStats");

        var totalRevenue = await db.Orders
            .Where(o => o.Status == OrderStatus.Delivered)
            .SumAsync(o => o.TotalAmount, ct);

        var orderCounts = await db.Orders
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var topProducts = await db.OrderItems
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(i => i.Quantity) })
            .OrderByDescending(x => x.TotalSold)
            .Take(5)
            .Join(db.Products, x => x.ProductId, p => p.Id,
                (x, p) => new { p.Name, x.TotalSold })
            .ToListAsync(ct);

        var customerCount = await db.Customers.CountAsync(ct);
        var productCount  = await db.Products.CountAsync(p => p.IsActive, ct);

        var stats = new
        {
            TotalRevenue = totalRevenue,
            CustomerCount = customerCount,
            ProductCount = productCount,
            OrdersByStatus = orderCounts,
            TopSellingProducts = topProducts
        };

        return JsonSerializer.Serialize(stats);
    }

    /// <summary>Execute a SAFE read-only summary query based on natural language intent.</summary>
    [KernelFunction(nameof(QueryByIntent))]
    [Description("Execute a predefined query based on intent. Supported intents: low_stock, recent_orders, top_customers, revenue_by_category.")]
    public async Task<string> QueryByIntent(
        [Description("Query intent: low_stock | recent_orders | top_customers | revenue_by_category")] string intent,
        CancellationToken ct = default)
    {
        logger.LogInformation("DB:QueryByIntent intent={Intent}", intent);

        return intent.ToLowerInvariant() switch
        {
            "low_stock" => await GetLowStockProducts(ct),
            "recent_orders" => await GetRecentOrders(ct),
            "top_customers" => await GetTopCustomers(ct),
            "revenue_by_category" => await GetRevenueByCategory(ct),
            _ => $"{{\"error\": \"Unknown intent '{intent}'. Use: low_stock, recent_orders, top_customers, revenue_by_category\"}}"
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> GetLowStockProducts(CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity < 20)
            .OrderBy(p => p.StockQuantity)
            .Select(p => new { p.Id, p.Name, p.StockQuantity, p.Category })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(new { intent = "low_stock", results = products });
    }

    private async Task<string> GetRecentOrders(CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .OrderByDescending(o => o.OrderDate)
            .Take(10)
            .Select(o => new
            {
                o.Id, o.Status, o.TotalAmount, o.OrderDate,
                CustomerName = o.Customer != null ? $"{o.Customer.FirstName} {o.Customer.LastName}" : "Unknown"
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(new { intent = "recent_orders", results = orders });
    }

    private async Task<string> GetTopCustomers(CancellationToken ct)
    {
        var customers = await db.Orders.AsNoTracking()
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, TotalSpend = g.Sum(o => o.TotalAmount), OrderCount = g.Count() })
            .OrderByDescending(x => x.TotalSpend)
            .Take(10)
            .Join(db.Customers, x => x.CustomerId, c => c.Id,
                (x, c) => new { c.FirstName, c.LastName, c.Email, x.TotalSpend, x.OrderCount })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(new { intent = "top_customers", results = customers });
    }

    private async Task<string> GetRevenueByCategory(CancellationToken ct)
    {
        var revenue = await db.OrderItems.AsNoTracking()
            .Join(db.Products, i => i.ProductId, p => p.Id, (i, p) => new { p.Category, Revenue = i.Quantity * i.UnitPrice })
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key ?? "Uncategorized", TotalRevenue = g.Sum(x => x.Revenue) })
            .OrderByDescending(x => x.TotalRevenue)
            .ToListAsync(ct);
        return JsonSerializer.Serialize(new { intent = "revenue_by_category", results = revenue });
    }
}
