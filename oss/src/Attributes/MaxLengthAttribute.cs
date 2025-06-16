using System;

namespace KsqlDsl.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class MaxLengthAttribute : Attribute
{
    public int Length { get; }

    public MaxLengthAttribute(int length)
    {
        if (length <= 0)
            throw new ArgumentException("最大長は1以上である必要があります", nameof(length));

        Length = length;
    }

    public override string ToString()
    {
        return $"MaxLength: {Length}";
    }
}
