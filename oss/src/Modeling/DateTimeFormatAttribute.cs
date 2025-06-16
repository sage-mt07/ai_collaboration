namespace KsqlDsl.Modeling;

using System;

[AttributeUsage(AttributeTargets.Property)]
public class DateTimeFormatAttribute : Attribute
{
    public string Format { get; set; }
    public string? Region { get; set; }

    public DateTimeFormatAttribute() { }
}
