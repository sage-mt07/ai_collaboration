namespace KsqlDsl
{
    public class KsqlWindowBuilder
    {
        public string Build(string expr)
        {
            if (expr.Contains("TumblingWindow")) return "WINDOW TUMBLING (SIZE 1 MINUTES)";
            if (expr.Contains("HoppingWindow")) return "WINDOW HOPPING (SIZE 5 MINUTES, ADVANCE BY 1 MINUTES)";
            if (expr.Contains("SessionWindow")) return "WINDOW SESSION (GAP 3 MINUTES)";
            return "WINDOW UNKNOWN";
        }
    }
}
