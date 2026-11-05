using System.Linq.Expressions;
using System.Reflection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.EntityFrameworkCore.Mapping;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.EntityFrameworkCore.Query;

/// <summary>
/// Normalizes a caller-supplied snapshot predicate into a shape Entity Framework can translate:
/// aggregate-type comparisons are folded away, culture-invariant string methods are mapped onto their
/// translatable counterparts, and scalar value-object member access is unwrapped.
/// </summary>
static class SnapshotPredicateRewriter
{
	/// <summary>
	/// Rewrites a predicate for execution against a snapshot store.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="whereClause">The caller-supplied predicate.</param>
	/// <param name="aggregateType">The aggregate type name the query is already scoped to.</param>
	/// <returns>The rewritten predicate.</returns>
	public static Expression<Func<T, bool>> Rewrite<T>(Expression<Func<T, bool>> whereClause, string aggregateType)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(whereClause);

		AggregateTypeExpressionVisitor aggregateTypeVisitor = new(aggregateType);
		var rewritten = (Expression<Func<T, bool>>)aggregateTypeVisitor.Visit(whereClause);
		InvariantStringMethodNormalizationVisitor invariantCasingVisitor = new();
		rewritten = (Expression<Func<T, bool>>)invariantCasingVisitor.Visit(rewritten);
		ScalarValueMemberAccessPredicateVisitor scalarVisitor = new();
		return (Expression<Func<T, bool>>)scalarVisitor.Visit(rewritten);
	}

	/// <summary>
	/// Throws when an ordering clause orders by the inner member of a scalar value object, which the
	/// snapshot stores cannot translate.
	/// </summary>
	/// <param name="queryExpression">The query expression to inspect.</param>
	public static void EnsureSupportedOrderByExpression(Expression queryExpression) =>
		new UnsupportedScalarValueOrderByDetector().Visit(queryExpression);

	sealed class AggregateTypeExpressionVisitor(string aggregateType) : ExpressionVisitor
	{
		readonly ConstantExpression _aggregateType = Expression.Constant(aggregateType, typeof(string));

		protected override Expression VisitMember(MemberExpression node) =>
			node.Member.Name == nameof(IAggregate.AggregateType)
			&& node.Expression is not null
			&& typeof(IAggregate).IsAssignableFrom(node.Expression.Type)
				? _aggregateType
				: base.VisitMember(node);
	}

	sealed class UnsupportedScalarValueOrderByDetector : ExpressionVisitor
	{
		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			if (
				node.Method.DeclaringType == typeof(Queryable)
				&& (
					node.Method.Name == nameof(Queryable.OrderBy)
					|| node.Method.Name == nameof(Queryable.OrderByDescending)
					|| node.Method.Name == nameof(Queryable.ThenBy)
					|| node.Method.Name == nameof(Queryable.ThenByDescending)
				)
			)
			{
				var lambda = (LambdaExpression)StripQuotes(node.Arguments[1]);
				new ScalarValueMemberAccessDetector().Visit(lambda.Body);
			}

			return base.VisitMethodCall(node);
		}

		static Expression StripQuotes(Expression expression)
		{
			while (expression.NodeType == ExpressionType.Quote)
				expression = ((UnaryExpression)expression).Operand;

			return expression;
		}
	}

	sealed class ScalarValueMemberAccessPredicateVisitor : ExpressionVisitor
	{
		protected override Expression VisitBinary(BinaryExpression node)
		{
			var visitedLeft = Visit(node.Left);
			var visitedRight = Visit(node.Right);

			return
				(node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual)
				&& TryRewriteScalarPrimitiveComparison(
					visitedLeft,
					visitedRight,
					out var rewrittenLeft,
					out var rewrittenRight
				)
				? Expression.MakeBinary(node.NodeType, rewrittenLeft, rewrittenRight)
				: node.Update(visitedLeft, node.Conversion, visitedRight);
		}

		protected override Expression VisitMember(MemberExpression node)
		{
			var visitedExpression = base.VisitMember(node);
			if (visitedExpression is not MemberExpression visited || visited.Expression is null)
				return visitedExpression;

			var scalarAttribute = visited.Expression.Type.GetCustomAttribute<ScalarAttribute>();
			if (scalarAttribute is null || visited.Member.Name != scalarAttribute.PropertyName)
				return visited;

			try
			{
				return Expression.Convert(visited.Expression, visited.Type);
			}
			catch (InvalidOperationException)
			{
				return visited;
			}
		}

		static bool TryRewriteScalarPrimitiveComparison(
			Expression left,
			Expression right,
			out Expression rewrittenLeft,
			out Expression rewrittenRight
		)
		{
			rewrittenLeft = left;
			rewrittenRight = right;

			if (
				TryGetScalarPropertyType(left.Type, out var leftScalarType)
				&& IsSameOrNullable(right.Type, leftScalarType)
			)
			{
				rewrittenLeft = Expression.Convert(left, right.Type);
				return true;
			}

			if (
				TryGetScalarPropertyType(right.Type, out var rightScalarType)
				&& IsSameOrNullable(left.Type, rightScalarType)
			)
			{
				rewrittenRight = Expression.Convert(right, left.Type);
				return true;
			}

			return false;
		}

		static bool TryGetScalarPropertyType(Type candidateType, out Type scalarPropertyType)
		{
			scalarPropertyType = null!;
			if (!SnapshotTypes.IsScalarValueObjectType(candidateType))
				return false;

			var scalarAttribute = candidateType.GetCustomAttribute<ScalarAttribute>()!;
			var scalarProperty = candidateType.GetProperty(
				scalarAttribute.PropertyName,
				BindingFlags.Instance | BindingFlags.Public
			);
			if (scalarProperty is null)
				return false;

			scalarPropertyType = scalarProperty.PropertyType;
			return true;
		}

		static bool IsSameOrNullable(Type candidate, Type expected)
		{
			var underlyingType = Nullable.GetUnderlyingType(candidate);
			return candidate == expected || underlyingType == expected;
		}
	}

	sealed class InvariantStringMethodNormalizationVisitor : ExpressionVisitor
	{
		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			var visitedObject = Visit(node.Object);
			var visitedArguments = Visit(node.Arguments);

			if (node.Method.DeclaringType == typeof(string) && node.Arguments.Count == 0 && visitedObject is not null)
			{
				if (node.Method.Name == nameof(string.ToLowerInvariant))
					return Expression.Call(visitedObject, nameof(string.ToLower), Type.EmptyTypes);

				if (node.Method.Name == nameof(string.ToUpperInvariant))
					return Expression.Call(visitedObject, nameof(string.ToUpper), Type.EmptyTypes);
			}

			return node.Update(visitedObject, visitedArguments);
		}
	}

	sealed class ScalarValueMemberAccessDetector : ExpressionVisitor
	{
		protected override Expression VisitMember(MemberExpression node)
		{
			var visited = base.VisitMember(node);
			if (node.Expression is null)
				return visited;

			var scalarAttribute = node.Expression.Type.GetCustomAttribute<ScalarAttribute>();
			return scalarAttribute is not null && node.Member.Name == scalarAttribute.PropertyName
				? throw new InvalidOperationException(
					$"Ordering by '{node.Expression.Type.Name}.{node.Member.Name}' is not supported for snapshot JSON queries. "
						+ "Order by the scalar value object property itself (for example: c => c.Name) instead of its inner scalar member."
				)
				: visited;
		}
	}
}
