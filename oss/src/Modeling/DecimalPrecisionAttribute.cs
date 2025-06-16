namespace KsqlDsl.Modeling;

using System;

[AttributeUsage(AttributeTargets.Property)]
public class DecimalPrecisionAttribute : Attribute
{
    public int Precision { get; }
    public int Scale { get; }

    public DecimalPrecisionAttribute(int precision, int scale)
    {
        Precision = precision;
        Scale = scale;
    }
}
