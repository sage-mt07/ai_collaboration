using System;

namespace KsqlDsl.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class DefaultValueAttribute : Attribute
{
    public object? Value { get; }

    public DefaultValueAttribute(object? value)
    {
        Value = value;
    }

    public override string ToString()
    {
        return $"DefaultValue: {Value ?? "null"}";
    }
}