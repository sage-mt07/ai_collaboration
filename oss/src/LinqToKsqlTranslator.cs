using KsqlDsl.Ksql;
using System;
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl;

internal class LinqToKsqlTranslator : ExpressionVisitor
{
    private readonly StringBuilder _ksqlBuilder = new();
    private string? _fromClause;
    private string? _selectClause;
    private string? _whereClause;
    private string? _groupByClause;
    private string? _havingClause;
    private string? _windowClause;
    private string? _joinClause;
    private string? _limitClause;
    private bool _hasAggregation = false;
    private bool _isAfterGroupBy = false;
    // 修正理由：外部フラグ制御方式に変更
    private bool _isPullQuery = false;

    public string Translate(Expression expression, string topicName, bool isPullQuery = false)
    {
        _fromClause = topicName;
        _selectClause = null;
        _whereClause = null;
        _groupByClause = null;
        _havingClause = null;
        _windowClause = null;
        _joinClause = null;
        _limitClause = null;
        _hasAggregation = false;
        _isAfterGroupBy = false;

        // 修正理由：外部から受け取ったフラグを設定
        _isPullQuery = isPullQuery;

        Visit(expression);

        return BuildKsqlQuery();
    }

    public string Translate(Expression expression, string topicName)
    {
        return Translate(expression, topicName, isPullQuery: false);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // まず子ノードを先に処理して、チェーンの流れを正しく追跡
        Visit(node.Arguments[0]);

        switch (node.Method.Name)
        {
            case "Where":
                if (node.Arguments.Count == 2)
                {
                    var whereExpression = UnwrapLambda(node.Arguments[1]);
                    if (whereExpression != null)
                    {
                        // GroupBy後のWhereはHAVING句になる
                        if (_hasAggregation && _isAfterGroupBy)
                        {
                            var havingBuilder = new KsqlHavingBuilder();
                            _havingClause = havingBuilder.Build(whereExpression);
                        }
                        else
                        {
                            var conditionBuilder = new KsqlConditionBuilder();
                            _whereClause = conditionBuilder.Build(whereExpression);
                            // 修正理由：内部判定ロジック削除（外部フラグ制御のため）
                            // _isPullQuery = true; // 削除
                        }
                    }
                }
                break;

            case "Select":
                if (node.Arguments.Count == 2)
                {
                    var selectExpression = UnwrapLambda(node.Arguments[1]);
                    if (selectExpression != null)
                    {
                        // GroupBy後のSelectは集約クエリとして処理
                        if (_hasAggregation && _isAfterGroupBy)
                        {
                            var aggregateBuilder = KsqlAggregateBuilder.Build(selectExpression);
                            _selectClause = aggregateBuilder;
                        }
                        else
                        {
                            var projectionBuilder = new KsqlProjectionBuilder();
                            _selectClause = projectionBuilder.Build(selectExpression);
                        }
                    }
                }
                break;

            case "GroupBy":
                if (node.Arguments.Count == 2)
                {
                    var groupByExpression = UnwrapLambda(node.Arguments[1]);
                    if (groupByExpression != null)
                    {
                        var groupByBuilder = KsqlGroupByBuilder.Build(groupByExpression);
                        _groupByClause = groupByBuilder;
                        _hasAggregation = true;
                        _isAfterGroupBy = true;
                        // 修正理由：内部判定ロジック削除（外部フラグ制御のため）
                        // _isPullQuery = false; // 削除
                    }
                }
                break;

            case "Take":
                // 修正理由：LIMIT句実装
                if (node.Arguments.Count == 2 && node.Arguments[1] is ConstantExpression limitConstant)
                {
                    var limitValue = limitConstant.Value;
                    if (limitValue is int intLimit)
                    {
                        _limitClause = $"LIMIT {intLimit}";
                        // 修正理由：内部判定ロジック削除（外部フラグ制御のため）
                        // _isPullQuery = true; // 削除
                    }
                }
                break;

            case "Skip":
                // 修正理由：ksqlDBではOFFSETサポートが限定的のため警告
                // 現在は実装せず、将来の拡張で対応
                break;

            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending":
                // ksqlDBではORDER BYはサポートされていない
                throw new NotSupportedException($"ORDER BY operations are not supported in ksqlDB: {node.Method.Name}");

            case "Join":
                if (node.Arguments.Count == 5)
                {
                    var joinBuilder = new KsqlJoinBuilder();
                    _joinClause = joinBuilder.Build(node);
                    // 修正理由：内部判定ロジック削除（外部フラグ制御のため）
                    // _isPullQuery = false; // 削除
                    return node;
                }
                break;

            case "Aggregate":
                if (node.Arguments.Count >= 2)
                {
                    _hasAggregation = true;
                    // 修正理由：内部判定ロジック削除（外部フラグ制御のため）
                    // _isPullQuery = false; // 削除
                }
                break;

            default:
                // 集約関数かどうかをチェック
                if (IsAggregateMethod(node.Method.Name))
                {
                    _hasAggregation = true;
                    // 修正理由：内部判定ロジック削除（外部フラグ制御のため）
                    // _isPullQuery = false; // 削除
                }
                break;
        }

        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        // ConstantExpressionは通常、クエリのルート（EventSet自体）を示す
        // 特別な処理は不要
        return node;
    }

    private Expression? UnwrapLambda(Expression expression)
    {
        return expression switch
        {
            LambdaExpression lambda => lambda.Body,
            UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lambda } => lambda.Body,
            _ => null
        };
    }

    private bool IsAggregateMethod(string methodName)
    {
        return methodName switch
        {
            "Sum" or "Count" or "Max" or "Min" or "Average" or "Avg" or
            "LatestByOffset" or "EarliestByOffset" or
            "CollectList" or "CollectSet" => true,
            _ => false
        };
    }

    private string BuildKsqlQuery()
    {
        var query = new StringBuilder();

        // JOINクエリの場合は特別処理
        if (!string.IsNullOrEmpty(_joinClause))
        {
            return _joinClause;
        }

        // SELECT句
        if (!string.IsNullOrEmpty(_selectClause))
        {
            query.Append(_selectClause);
        }
        else
        {
            query.Append("SELECT *");
        }

        // FROM句
        query.Append($" FROM {_fromClause}");

        // WHERE句
        if (!string.IsNullOrEmpty(_whereClause))
        {
            query.Append($" {_whereClause}");
        }

        // GROUP BY句
        if (!string.IsNullOrEmpty(_groupByClause))
        {
            query.Append($" {_groupByClause}");
        }

        // WINDOW句
        if (!string.IsNullOrEmpty(_windowClause))
        {
            query.Append($" {_windowClause}");
        }

        // HAVING句
        if (!string.IsNullOrEmpty(_havingClause))
        {
            query.Append($" {_havingClause}");
        }

        // LIMIT句（Pull Queryの場合）
        if (!string.IsNullOrEmpty(_limitClause))
        {
            query.Append($" {_limitClause}");
        }

        // 修正理由：フラグ制御方式でEMIT句制御
        // Pull QueryにはEMIT句を付けない、Push QueryにはEMIT CHANGESを付ける
        if (!_isPullQuery)
        {
            // Push Query（ストリーミング）の場合のみEMIT CHANGES
            query.Append(" EMIT CHANGES");
        }
        // Pull Queryの場合はEMIT句なし（瞬時実行）

        return query.ToString();
    }

    public bool IsPullQuery()
    {
        return _isPullQuery;
    }

    public string GetDiagnostics()
    {
        var diagnostics = new StringBuilder();
        diagnostics.AppendLine($"Query Type: {(IsPullQuery() ? "Pull Query" : "Push Query")}");
        diagnostics.AppendLine($"Has Aggregation: {_hasAggregation}");
        diagnostics.AppendLine($"FROM: {_fromClause}");
        diagnostics.AppendLine($"SELECT: {_selectClause ?? "SELECT *"}");
        diagnostics.AppendLine($"WHERE: {_whereClause ?? "None"}");
        diagnostics.AppendLine($"GROUP BY: {_groupByClause ?? "None"}");
        diagnostics.AppendLine($"HAVING: {_havingClause ?? "None"}");
        diagnostics.AppendLine($"LIMIT: {_limitClause ?? "None"}");
        return diagnostics.ToString();
    }
}