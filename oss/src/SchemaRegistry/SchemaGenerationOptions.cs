namespace KsqlDsl.SchemaRegistry;

public class SchemaGenerationOptions
{
    public string? CustomName { get; set; }

    public string? Namespace { get; set; }

    public string? Documentation { get; set; }

    public bool PrettyFormat { get; set; } = true;

    public bool UseKebabCase { get; set; } = false;
}