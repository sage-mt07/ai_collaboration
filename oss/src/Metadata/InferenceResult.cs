namespace KsqlDsl.Metadata;



internal class InferenceResult
{
    public StreamTableType InferredType { get; set; }
    public bool IsExplicitlyDefined { get; set; }
    public string Reason { get; set; } = "";
}