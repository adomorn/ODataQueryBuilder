using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace ODataQueryBuilder
{
    public static class ODataQueryBuilder
    {
        public static string BuildODataQuery<T>(
            Expression<Func<T, bool>> whereExpression,
            int? top = null,
            int? skip = null,
            Expression<Func<T, object>> orderByExpression = null,
            params Expression<Func<T, object>>[] includes)
        {
            var odataQuery = new StringBuilder();

            if (whereExpression != null)
            {
                var filterClause = ConvertToFilterClause(whereExpression);

                if (!string.IsNullOrEmpty(filterClause))
                {
                    odataQuery.Append($"$filter={filterClause}&");
                }
            }

            if (orderByExpression != null)
            {
                var orderByClause = ConvertToOrderByClause(orderByExpression);

                if (!string.IsNullOrEmpty(orderByClause))
                {
                    odataQuery.Append($"$orderby={orderByClause}&");
                }
            }

            if (skip.HasValue)
            {
                odataQuery.Append($"$skip={skip.Value}&");
            }

            if (top.HasValue)
            {
                odataQuery.Append($"$top={top.Value}&");
            }

            if (includes != null && includes.Length > 0)
            {
                var expandClause = string.Join(",", includes.Select(ConvertToExpandClause));
                odataQuery.Append($"$expand={expandClause}&");
            }

            return odataQuery.ToString().TrimEnd('&');
        }

        private static string ConvertToFilterClause<T>(Expression<Func<T, bool>> expression)
        {
            var odataExpressionVisitor = new ODataExpressionVisitor();

            return odataExpressionVisitor.Translate(expression.Body);
        }

        private static string ConvertToOrderByClause(Expression expression)
        {
            if (expression is UnaryExpression unaryExpression)
            {
                return GetMemberPath((MemberExpression)unaryExpression.Operand);
            }
            else if (expression is LambdaExpression lambdaExpression &&
                     lambdaExpression.Body is MemberExpression memberExpression)
            {
                return GetMemberPath(memberExpression);
            }
            else if (expression is MemberExpression memberExpr)
            {
                return GetMemberPath(memberExpr);
            }
            else
            {
                throw new InvalidCastException("Unsupported expression type for $orderby clause.");
            }
        }

        private static string ConvertToExpandClause(Expression expression)
        {
            if (expression is UnaryExpression unaryExpression)
            {
                return GetMemberPath((MemberExpression)unaryExpression.Operand);
            }
            else if (expression is LambdaExpression lambdaExpression &&
                     lambdaExpression.Body is MemberExpression memberExpression)
            {
                return GetMemberPath(memberExpression);
            }
            else if (expression is MemberExpression memberExpr)
            {
                return GetMemberPath(memberExpr);
            }
            else
            {
                throw new InvalidCastException("Unsupported expression type for $expand clause.");
            }
        }

        private static string GetMemberPath(MemberExpression memberExpression)
        {
            var path = memberExpression.Member.Name;
            var expression = memberExpression.Expression;

            while (expression is MemberExpression innerMember)
            {
                path = $"{innerMember.Member.Name}/{path}";
                expression = innerMember.Expression;
            }

            return path;
        }

        sealed class ODataExpressionVisitor : ExpressionVisitor
        {
            private readonly StringBuilder _filterClause = new StringBuilder();

            private string _parameterName;

            public string Translate(Expression expression)
            {
                Visit(expression);

                return _filterClause.ToString();
            }

            protected override Expression VisitBinary(BinaryExpression node)
            {
                var needsParentheses = IsComplexExpression(node);

                if (needsParentheses)
                {
                    _filterClause.Append("(");
                }

                Visit(node.Left);
                _filterClause.Append($" {GetODataOperator(node.NodeType)} ");
                Visit(node.Right);

                if (needsParentheses)
                {
                    _filterClause.Append(")");
                }

                return node;
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                if (node.Expression != null && node.Expression.NodeType == ExpressionType.MemberAccess)
                {
                    Visit(node.Expression);
                    _filterClause.Append("/");
                }
                else if (!string.IsNullOrEmpty(_parameterName))
                {
                    _filterClause.Append($"{_parameterName}/");
                }

                _filterClause.Append(node.Member.Name);

                return node;
            }

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Type == typeof(string))
                {
                    _filterClause.Append($"'{node.Value}'");
                }
                else
                {
                    _filterClause.Append(node.Value);
                }

                return node;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                _parameterName = node.Name;

                return base.VisitParameter(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.Name == "Any")
                {
                    var lambda = (LambdaExpression)StripQuotes(node.Arguments[1]);
                    var collectionPath = GetVisitorMemberPath((MemberExpression)node.Arguments[0]);
                    _filterClause.Append($"{collectionPath}/any({lambda.Parameters[0].Name}: ");
                    _parameterName = lambda.Parameters[0].Name;
                    Visit(lambda.Body);
                    _filterClause.Append(")");
                    _parameterName = null;

                    return node;
                }

                return base.VisitMethodCall(node);
            }

            private static string GetODataOperator(ExpressionType expressionType)
            {
                switch (expressionType)
                {
                    case ExpressionType.Equal:
                        return "eq";
                    case ExpressionType.NotEqual:
                        return "ne";
                    case ExpressionType.GreaterThan:
                        return "gt";
                    case ExpressionType.GreaterThanOrEqual:
                        return "ge";
                    case ExpressionType.LessThan:
                        return "lt";
                    case ExpressionType.LessThanOrEqual:
                        return "le";
                    case ExpressionType.AndAlso:
                        return "and";
                    case ExpressionType.OrElse:
                        return "or";
                    default:
                        throw new NotSupportedException($"Operator {expressionType} is not supported.");
                }
            }

            private static string GetVisitorMemberPath(MemberExpression memberExpression)
            {
                var path = memberExpression.Member.Name;
                var expression = memberExpression.Expression;

                while (expression is MemberExpression innerMember)
                {
                    path = $"{innerMember.Member.Name}/{path}";
                    expression = innerMember.Expression;
                }

                return path;
            }

            private static Expression StripQuotes(Expression e)
            {
                while (e.NodeType == ExpressionType.Quote)
                {
                    e = ((UnaryExpression)e).Operand;
                }

                return e;
            }

            private static bool IsComplexExpression(BinaryExpression expression)
            {
                return expression.Left is BinaryExpression || expression.Right is BinaryExpression;
            }
        }
    }
}
