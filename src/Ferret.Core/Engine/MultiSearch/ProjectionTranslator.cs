using System.Linq.Expressions;
using System.Reflection;

namespace Ferret.Core.Engine.MultiSearch;

/// <summary>A multisearch projection compiled against one entity: the columns to select and the hit factory.</summary>
internal sealed class CompiledProjection
{
    /// <summary>Slot columns in read order; slot i's value feeds the i-th entity-property reference in the projection.</summary>
    public required IReadOnlyList<string> ColumnNames { get; init; }

    /// <summary>Builds a hit from DBNull-normalized slot values (same order as <see cref="ColumnNames"/>).</summary>
    public required Func<object?[], MultiSearchHit> Materializer { get; init; }
}

/// <summary>
/// Translates a multisearch projection lambda (new-expression or member-init over plain entity
/// property accesses and constants) into slot columns plus a compiled materializer.
/// </summary>
internal static class ProjectionTranslator
{
    public static CompiledProjection Translate(LambdaExpression projection, EntityModel model)
    {
        if (projection.Body is not (NewExpression or MemberInitExpression))
        {
            throw new NotSupportedException(
                $"Multisearch projection for {model.ClrType.Name} must be a new-expression or member-init; got: {projection.Body}");
        }

        var entityParam = projection.Parameters[0];
        var valuesParam = Expression.Parameter(typeof(object?[]), "values");
        var columns = new List<string>();
        var visitor = new SlotRewriter(entityParam, valuesParam, model, columns);

        if (projection.Body is MemberInitExpression init)
        {
            foreach (var binding in init.Bindings)
            {
                if (binding is not MemberAssignment)
                {
                    throw new NotSupportedException(
                        $"Multisearch projection supports simple member assignments only; got: {binding}");
                }
                if (binding.Member.DeclaringType == typeof(MultiSearchHit))
                {
                    throw new NotSupportedException(
                        $"Multisearch projection may not assign engine-owned member '{binding.Member.Name}'.");
                }
            }
        }

        var rewrittenBody = visitor.Visit(projection.Body);
        var materializer = Expression.Lambda<Func<object?[], MultiSearchHit>>(
            Expression.Convert(rewrittenBody, typeof(MultiSearchHit)), valuesParam).Compile();

        return new CompiledProjection { ColumnNames = columns, Materializer = materializer };
    }

    /// <summary>
    /// Replaces each entity-parameter property access with a typed read from the values array,
    /// assigning slots in visit order. Rejects any other reference to the entity parameter.
    /// </summary>
    private sealed class SlotRewriter : ExpressionVisitor
    {
        private readonly ParameterExpression _entityParam;
        private readonly ParameterExpression _valuesParam;
        private readonly EntityModel _model;
        private readonly List<string> _columns;

        public SlotRewriter(
            ParameterExpression entityParam, ParameterExpression valuesParam,
            EntityModel model, List<string> columns)
        {
            _entityParam = entityParam;
            _valuesParam = valuesParam;
            _model = model;
            _columns = columns;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != _entityParam)
            {
                if (DependsOnEntityParam(node))
                {
                    throw new NotSupportedException(
                        $"Multisearch projection supports plain property accesses on the entity parameter only; got: {node}");
                }
                return base.VisitMember(node);
            }

            if (node.Member is not PropertyInfo property
                || !_model.ColumnByPropertyName.TryGetValue(property.Name, out var column))
            {
                throw new NotSupportedException(
                    $"Property '{node.Member.Name}' on {_model.ClrType.Name} has no mapped column.");
            }

            var slot = _columns.Count;
            _columns.Add(column);
            return Expression.Convert(
                Expression.ArrayIndex(_valuesParam, Expression.Constant(slot)), node.Type);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (DependsOnEntityParam(node))
            {
                throw new NotSupportedException(
                    $"Multisearch projection cannot translate method calls; got: {node}");
            }
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _entityParam)
            {
                throw new NotSupportedException(
                    "Multisearch projection may reference the entity parameter only through plain property accesses.");
            }
            return base.VisitParameter(node);
        }

        private bool DependsOnEntityParam(Expression node)
        {
            var finder = new ParameterFinder(_entityParam);
            finder.Visit(node);
            return finder.Found;
        }
    }

    private sealed class ParameterFinder : ExpressionVisitor
    {
        private readonly ParameterExpression _target;

        public ParameterFinder(ParameterExpression target) => _target = target;

        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _target)
            {
                Found = true;
            }
            return base.VisitParameter(node);
        }
    }
}
