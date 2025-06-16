namespace KsqlDsl.SchemaRegistry;


public class AvroField
{
    public string Name { get; set; } = string.Empty;
    public object Type { get; set; } = string.Empty;
    public string? Doc { get; set; }
    public object? Default { get; set; }
}
