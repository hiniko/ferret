using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ferret.AspNetCore.Binding;

/// <summary>
/// Binds repeated <c>filter=field:op:value</c> query parameters into <see cref="IReadOnlyList{FilterClause}"/>.
/// Operators: <c>eq</c>, <c>neq</c>, <c>contains</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>, <c>in</c>.
/// For <c>in</c>, value is a comma-separated list: <c>filter=category:in:tools,garden</c>.
/// </summary>
public sealed class FilterClauseListBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var values = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (values == ValueProviderResult.None)
        {
            bindingContext.Result = ModelBindingResult.Success(Array.Empty<FilterClause>());
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(ClauseParsing.ParseFilters(values));
        return Task.CompletedTask;
    }
}
