using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814

namespace AI.ChatAgent.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    StockQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TokenCount = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OrderDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Seed data
            migrationBuilder.InsertData("Products", new[] { "Id","Name","Description","Price","Category","StockQuantity","CreatedAt","IsActive" }, new object[,]
            {
                { 1, "Laptop Pro 15",       "High-performance laptop",        1299.99m, "Electronics", 45,  DateTimeOffset.UtcNow, true },
                { 2, "Wireless Mouse",      "Ergonomic wireless mouse",        49.99m,  "Accessories", 200, DateTimeOffset.UtcNow, true },
                { 3, "USB-C Hub",           "7-in-1 USB-C hub",                79.99m,  "Accessories", 150, DateTimeOffset.UtcNow, true },
                { 4, "Mechanical Keyboard", "RGB mechanical keyboard",        149.99m,  "Accessories",  80, DateTimeOffset.UtcNow, true },
                { 5, "Monitor 27\" 4K",     "4K IPS monitor",                 599.99m,  "Electronics",  30, DateTimeOffset.UtcNow, true },
                { 6, "Webcam HD",           "1080p USB webcam with mic",       89.99m,  "Electronics", 120, DateTimeOffset.UtcNow, true },
                { 7, "SSD 1TB",             "NVMe M.2 SSD",                   109.99m,  "Storage",     200, DateTimeOffset.UtcNow, true },
                { 8, "RAM 32GB Kit",        "DDR5 6000MHz dual channel",      139.99m,  "Memory",       90, DateTimeOffset.UtcNow, true }
            });

            migrationBuilder.InsertData("Customers", new[] { "Id","FirstName","LastName","Email","Phone","CreatedAt" }, new object[,]
            {
                { 1, "Alice", "Johnson",  "alice@example.com", "+1-555-0101", DateTimeOffset.UtcNow },
                { 2, "Bob",   "Williams", "bob@example.com",   "+1-555-0102", DateTimeOffset.UtcNow },
                { 3, "Carol", "Davis",    "carol@example.com", "+1-555-0103", DateTimeOffset.UtcNow },
                { 4, "David", "Martinez", "david@example.com", "+1-555-0104", DateTimeOffset.UtcNow }
            });

            // Indexes
            migrationBuilder.CreateIndex("IX_ChatMessages_SessionId",    "ChatMessages", "SessionId");
            migrationBuilder.CreateIndex("IX_ChatMessages_CreatedAt",    "ChatMessages", "CreatedAt");
            migrationBuilder.CreateIndex("IX_Customers_Email",           "Customers",    "Email", unique: true);
            migrationBuilder.CreateIndex("IX_Orders_CustomerId",         "Orders",       "CustomerId");
            migrationBuilder.CreateIndex("IX_Orders_Status",             "Orders",       "Status");
            migrationBuilder.CreateIndex("IX_Orders_OrderDate",          "Orders",       "OrderDate");
            migrationBuilder.CreateIndex("IX_OrderItems_OrderId",        "OrderItems",   "OrderId");
            migrationBuilder.CreateIndex("IX_OrderItems_ProductId",      "OrderItems",   "ProductId");
            migrationBuilder.CreateIndex("IX_Products_Category",         "Products",     "Category");
            migrationBuilder.CreateIndex("IX_Products_IsActive",         "Products",     "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("OrderItems");
            migrationBuilder.DropTable("Orders");
            migrationBuilder.DropTable("ChatMessages");
            migrationBuilder.DropTable("Customers");
            migrationBuilder.DropTable("Products");
            migrationBuilder.DropTable("ChatSessions");
        }
    }
}
