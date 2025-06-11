using System;
using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl.Modeling;

namespace KsqlDsl.Tests
{
    /// <summary>
    /// Shared test entities for use across all test classes
    /// </summary>
    
    /// <summary>
    /// Order entity for testing KSQL translation
    /// </summary>
    public class Order
    {
        public int OrderId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public double Score { get; set; }
        public decimal Price { get; set; }
        public DateTime OrderDate { get; set; }
        public string Region { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool? IsProcessed { get; set; }
        public int Quantity { get; set; }
        public string ProductId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Customer entity for testing KSQL translation
    /// </summary>
    public class Customer
    {
        public string CustomerId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool? IsVerified { get; set; }
        public int? Age { get; set; }
        public DateTime? LastLoginDate { get; set; }
    }

    /// <summary>
    /// Product entity for testing KSQL translation
    /// </summary>
    public class Product
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
        public Guid ProductGuid { get; set; }
    }

    /// <summary>
    /// Order entity with specific configurations for testing
    /// </summary>
    public class OrderEntity
    {
        public string Id { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsActive { get; set; }
        public bool? IsProcessed { get; set; }
    }

    /// <summary>
    /// Customer entity for JOIN testing
    /// </summary>
    public class CustomerEntity
    {
        public string Id { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool? IsVerified { get; set; }
    }




    /// <summary>
    /// Order entity for schema registry testing
    /// </summary>
    public class OrderEntityForRegistry
    {
        public int OrderId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime OrderDate { get; set; }
        public bool IsProcessed { get; set; }
        
        [KafkaIgnore(Reason = "Internal tracking")]
        public DateTime InternalTimestamp { get; set; }
        
        [KafkaIgnore]
        public string DebugInfo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Product entity for schema registry testing
    /// </summary>
    public class ProductEntityForRegistry
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
        public Guid ProductGuid { get; set; }
    }

    /// <summary>
    /// Customer entity with nullable properties
    /// </summary>
    public class CustomerEntityWithNullables
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int? Age { get; set; }
        public DateTime? LastLoginDate { get; set; }
        public bool? IsVerified { get; set; }
        
        [KafkaIgnore]
        public string? InternalNotes { get; set; }
    }

    /// <summary>
    /// Statistics entity for aggregation testing
    /// </summary>
    public class CustomerStats
    {
        public string CustomerId { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public int OrderCount { get; set; }
    }

    /// <summary>
    /// Latest order entity for aggregation testing
    /// </summary>
    public class CustomerLatestOrder
    {
        public string CustomerId { get; set; } = string.Empty;
        public string LatestOrderId { get; set; } = string.Empty;
        public DateTime LatestOrderTime { get; set; }
        public decimal LatestAmount { get; set; }
    }

    /// <summary>
    /// First order entity for aggregation testing
    /// </summary>
    public class CustomerFirstOrder
    {
        public string CustomerId { get; set; } = string.Empty;
        public string FirstOrderId { get; set; } = string.Empty;
        public DateTime FirstOrderTime { get; set; }
        public decimal FirstAmount { get; set; }
    }

    /// <summary>
    /// Hourly statistics entity for windowed aggregation testing
    /// </summary>
    public class HourlyStats
    {
        public string CustomerId { get; set; } = string.Empty;
        public DateTime Hour { get; set; }
        public int OrderCount { get; set; }
    }
}