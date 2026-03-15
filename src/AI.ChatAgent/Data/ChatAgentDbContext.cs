using AI.ChatAgent.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.ChatAgent.Data;

/// <summary>
/// Application database context for chat history, products, customers and orders.
/// Supports both SQLite (dev) and SQL Server (production).
/// </summary>
public sealed class ChatAgentDbContext(DbContextOptions<ChatAgentDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Product ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Price).HasColumnType("decimal(18,2)");
            b.HasIndex(p => p.Category);
            b.HasIndex(p => p.IsActive);
        });

        // ── Customer ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.Email).IsUnique();
            b.HasMany(c => c.Orders)
             .WithOne(o => o.Customer)
             .HasForeignKey(o => o.CustomerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Order ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
            b.HasIndex(o => o.Status);
            b.HasIndex(o => o.OrderDate);
            b.HasMany(o => o.Items)
             .WithOne(i => i.Order)
             .HasForeignKey(i => i.OrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── OrderItem ─────────────────────────────────────────────────────────
        modelBuilder.Entity<OrderItem>(b =>
        {
            b.HasKey(i => i.Id);
            b.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
            b.HasOne(i => i.Product)
             .WithMany()
             .HasForeignKey(i => i.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ChatSession ───────────────────────────────────────────────────────
        modelBuilder.Entity<ChatSession>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasMany(s => s.Messages)
             .WithOne(m => m.Session)
             .HasForeignKey(m => m.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ChatMessage ───────────────────────────────────────────────────────
        modelBuilder.Entity<ChatMessage>(b =>
        {
            b.HasKey(m => m.Id);
            b.HasIndex(m => m.SessionId);
            b.HasIndex(m => m.CreatedAt);
        });

        // ── Seed Data ─────────────────────────────────────────────────────────
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Laptop Pro 15",       Description = "High-performance laptop",          Price = 1299.99m, Category = "Electronics", StockQuantity = 45,  IsActive = true },
            new Product { Id = 2, Name = "Wireless Mouse",      Description = "Ergonomic wireless mouse",         Price = 49.99m,   Category = "Accessories", StockQuantity = 200, IsActive = true },
            new Product { Id = 3, Name = "USB-C Hub",           Description = "7-in-1 USB-C hub",                Price = 79.99m,   Category = "Accessories", StockQuantity = 150, IsActive = true },
            new Product { Id = 4, Name = "Mechanical Keyboard", Description = "RGB mechanical keyboard",         Price = 149.99m,  Category = "Accessories", StockQuantity = 80,  IsActive = true },
            new Product { Id = 5, Name = "Monitor 27\" 4K",     Description = "4K IPS monitor",                  Price = 599.99m,  Category = "Electronics", StockQuantity = 30,  IsActive = true },
            new Product { Id = 6, Name = "Webcam HD",           Description = "1080p USB webcam with mic",       Price = 89.99m,   Category = "Electronics", StockQuantity = 120, IsActive = true },
            new Product { Id = 7, Name = "SSD 1TB",             Description = "NVMe M.2 SSD",                    Price = 109.99m,  Category = "Storage",     StockQuantity = 200, IsActive = true },
            new Product { Id = 8, Name = "RAM 32GB Kit",        Description = "DDR5 6000MHz dual channel",       Price = 139.99m,  Category = "Memory",      StockQuantity = 90,  IsActive = true }
        );

        modelBuilder.Entity<Customer>().HasData(
            new Customer { Id = 1, FirstName = "Alice",   LastName = "Johnson",   Email = "alice@example.com",   Phone = "+1-555-0101" },
            new Customer { Id = 2, FirstName = "Bob",     LastName = "Williams",  Email = "bob@example.com",     Phone = "+1-555-0102" },
            new Customer { Id = 3, FirstName = "Carol",   LastName = "Davis",     Email = "carol@example.com",   Phone = "+1-555-0103" },
            new Customer { Id = 4, FirstName = "David",   LastName = "Martinez",  Email = "david@example.com",   Phone = "+1-555-0104" }
        );

        modelBuilder.Entity<Order>().HasData(
            new Order { Id = 1, CustomerId = 1, TotalAmount = 1349.98m, Status = OrderStatus.Delivered, OrderDate = DateTimeOffset.UtcNow.AddDays(-30) },
            new Order { Id = 2, CustomerId = 2, TotalAmount = 229.98m,  Status = OrderStatus.Shipped,   OrderDate = DateTimeOffset.UtcNow.AddDays(-5)  },
            new Order { Id = 3, CustomerId = 3, TotalAmount = 599.99m,  Status = OrderStatus.Confirmed, OrderDate = DateTimeOffset.UtcNow.AddDays(-1)  },
            new Order { Id = 4, CustomerId = 1, TotalAmount = 109.99m,  Status = OrderStatus.Pending,   OrderDate = DateTimeOffset.UtcNow               }
        );

        modelBuilder.Entity<OrderItem>().HasData(
            new OrderItem { Id = 1, OrderId = 1, ProductId = 1, Quantity = 1, UnitPrice = 1299.99m },
            new OrderItem { Id = 2, OrderId = 1, ProductId = 2, Quantity = 1, UnitPrice =   49.99m },
            new OrderItem { Id = 3, OrderId = 2, ProductId = 4, Quantity = 1, UnitPrice =  149.99m },
            new OrderItem { Id = 4, OrderId = 2, ProductId = 2, Quantity = 1, UnitPrice =   49.99m },
            new OrderItem { Id = 5, OrderId = 3, ProductId = 5, Quantity = 1, UnitPrice =  599.99m },
            new OrderItem { Id = 6, OrderId = 4, ProductId = 7, Quantity = 1, UnitPrice =  109.99m }
        );
    }
}
