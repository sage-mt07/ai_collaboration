using System;

namespace KsqlDsl.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class KeyAttribute : Attribute
{
    public int Order { get; set; } = 0;

    public string? Encoding { get; set; }

    public KeyAttribute()
    {
    }

    public KeyAttribute(int order)
    {
        Order = order;
    }

    public override string ToString()
    {
        var encoding = string.IsNullOrEmpty(Encoding) ? "Default" : Encoding;
        return $"Key (Order: {Order}, Encoding: {encoding})";
    }
}