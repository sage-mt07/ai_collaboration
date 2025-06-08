using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Ksql;

internal class KsqlConditionBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();
    private bool _includeParameterPrefix = false;

    public string Build(Expression expression)
    {
        _includeParameterPrefix = false;
        _sb.Clear();
        Visit(expression);
        return "WHERE " + _sb.ToString();
    }

    /// <summary>
    /// Builds a condition without the "WHERE" prefix, useful for JOIN conditions
    /// </summary>
    /// <param name="expression">The expression to build</param>
    /// <returns>The condition string without "WHERE" prefix</returns>
    public string BuildCondition(Expression expression)
    {
        _includeParameterPrefix = true;
        _sb.Clear();
        Visit(expression);
        return _sb.ToString();
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        // Handle composite key joins: new { a.Id, a.Type } equals new { b.Id, b.Type }
        if (node.NodeType == ExpressionType.Equal &&
            node.Left is NewExpression leftNew &&
            node.Right is NewExpression rightNew)
        {
            BuildCompositeKeyCondition(leftNew, rightNew);
            return node;
        }

        // Special case: bool member == true (avoid double bool processing)
        if (node.NodeType == ExpressionType.Equal &&
            node.Left is MemberExpression leftMember &&
            (leftMember.Type == typeof(bool) || leftMember.Type == typeof(bool?)) &&
            node.Right is ConstantExpression rightConstant &&
            rightConstant.Value is bool boolValue)
        {
            string memberName;
            if (_includeParameterPrefix && leftMember.Expression is ParameterExpression param)
            {
                memberName = $"{param.Name}.{leftMember.Member.Name}";
            }
            else
            {
                memberName = leftMember.Member.Name;
            }

            _sb.Append("(");
            _sb.Append(memberName);
            _sb.Append(boolValue ? " = true" : " = false");
            _sb.Append(")");
            return node;
        }

        // Handle regular binary expressions
        _sb.Append("(");
        Visit(node.Left);
        _sb.Append(" " + GetSqlOperator(node.NodeType) + " ");
        Visit(node.Right);
        _sb.Append(")");
        return node;
    }

    /// <summary>
    /// Builds conditions for composite key comparisons from NewExpressions
    /// </summary>
    /// <param name="leftNew">Left side NewExpression (e.g., new { a.Id, a.Type })</param>
    /// <param name="rightNew">Right side NewExpression (e.g., new { b.Id, b.Type })</param>
    private void BuildCompositeKeyCondition(NewExpression leftNew, NewExpression rightNew)
    {
        if (leftNew.Arguments.Count != rightNew.Arguments.Count)
        {
            throw new InvalidOperationException("Composite key expressions must have the same number of properties");
        }

        if (leftNew.Arguments.Count == 0)
        {
            throw new InvalidOperationException("Composite key expressions must have at least one property");
        }

        var conditions = new List<string>();

        for (int i = 0; i < leftNew.Arguments.Count; i++)
        {
            var leftMember = ExtractMemberName(leftNew.Arguments[i]);
            var rightMember = ExtractMemberName(rightNew.Arguments[i]);

            if (leftMember == null || rightMember == null)
            {
                throw new InvalidOperationException($"Unable to extract member names from composite key at index {i}");
            }

            conditions.Add($"{leftMember} = {rightMember}");
        }

        // Join all conditions with AND, wrap in parentheses for complex expressions
        if (conditions.Count == 1)
        {
            _sb.Append(conditions[0]);
        }
        else
        {
            _sb.Append("(");
            _sb.Append(string.Join(" AND ", conditions));
            _sb.Append(")");
        }
    }

    /// <summary>
    /// Extracts member name from an expression, handling parameter prefixes
    /// NOTE: Bool formatting is handled in VisitMember to avoid double formatting
    /// </summary>
    /// <param name="expression">The expression to extract from</param>
    /// <returns>The member name with parameter prefix (e.g., "a.Id") when _includeParameterPrefix is true</returns>
    private string ExtractMemberName(Expression expression)
    {
        return expression switch
        {
            MemberExpression member when _includeParameterPrefix && member.Expression is ParameterExpression param =>
                $"{param.Name}.{member.Member.Name}",
            MemberExpression member => member.Member.Name,
            UnaryExpression unary => ExtractMemberName(unary.Operand),
            _ => null
        };
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        switch (node.NodeType)
        {
            case ExpressionType.Not:
                // Handle nullable bool negation: !o.IsProcessed.Value → "(IsProcessed = false)"
                if (node.Operand is MemberExpression member &&
                    member.Member.Name == "Value" &&
                    member.Expression is MemberExpression innerMember &&
                    innerMember.Type == typeof(bool?))
                {
                    string memberName;
                    if (_includeParameterPrefix && innerMember.Expression is ParameterExpression param)
                    {
                        memberName = $"{param.Name}.{innerMember.Member.Name}";
                    }
                    else
                    {
                        memberName = innerMember.Member.Name;
                    }
                    _sb.Append("(");
                    _sb.Append(memberName);
                    _sb.Append(" = false");
                    _sb.Append(")");
                    return node;
                }
                // Handle regular boolean negation: !o.IsActive → "(IsActive = false)"
                else if (node.Operand is MemberExpression regularMember &&
                    (regularMember.Type == typeof(bool) || regularMember.Type == typeof(bool?)))
                {
                    string memberName;
                    if (_includeParameterPrefix && regularMember.Expression is ParameterExpression param)
                    {
                        memberName = $"{param.Name}.{regularMember.Member.Name}";
                    }
                    else
                    {
                        memberName = regularMember.Member.Name;
                    }
                    _sb.Append("(");
                    _sb.Append(memberName);
                    _sb.Append(" = false");
                    _sb.Append(")");
                    return node;
                }
                break;

            case ExpressionType.Convert:
            case ExpressionType.ConvertChecked:
                // Handle type conversions by visiting the operand
                return Visit(node.Operand);
        }

        // Default handling for other unary expressions
        return base.VisitUnary(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // Handle nullable bool .Value access: o.IsProcessed.Value → "(IsProcessed = true)"
        if (node.Member.Name == "Value" &&
            node.Expression is MemberExpression innerMember &&
            innerMember.Type == typeof(bool?))
        {
            // Build the member name for nullable bool .Value access
            string memberName;
            if (_includeParameterPrefix && innerMember.Expression is ParameterExpression param)
            {
                memberName = $"{param.Name}.{innerMember.Member.Name}";
            }
            else
            {
                memberName = innerMember.Member.Name;
            }

            _sb.Append("(");
            _sb.Append(memberName);
            _sb.Append(" = true");
            _sb.Append(")");
            return node;
        }

        // Build the member name with or without parameter prefix
        string finalMemberName;
        if (_includeParameterPrefix && node.Expression is ParameterExpression parameter)
        {
            finalMemberName = $"{parameter.Name}.{node.Member.Name}";
        }
        else
        {
            finalMemberName = node.Member.Name;
        }

        // Handle bool and nullable bool properties explicitly with parentheses
        if (node.Type == typeof(bool) || node.Type == typeof(bool?))
        {
            _sb.Append("(");
            _sb.Append(finalMemberName);
            _sb.Append(" = true");
            _sb.Append(")");
        }
        else
        {
            _sb.Append(finalMemberName);
        }

        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == typeof(string))
            _sb.Append($"'{node.Value}'");
        else if (node.Type == typeof(bool))
            _sb.Append(node.Value.ToString().ToLower());
        else
            _sb.Append(node.Value);
        return node;
    }

    private string GetSqlOperator(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Equal => "=",
        ExpressionType.NotEqual => "<>",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        ExpressionType.AndAlso => "AND",
        ExpressionType.OrElse => "OR",
        _ => throw new NotSupportedException($"Unsupported operator: {nodeType}")
    };
}