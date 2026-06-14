using Ferret.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ferret.AspNetCore.Binding;

/// <summary>
/// Binds repeated <c>sort=field:direction</c> query parameters into <see cref="IReadOnlyList{SortClause}"/>.
/// Direction: <c>asc</c>, <c>desc</c>; defaults to <c>asc</c> when omitted.
/// </summary>
public sealed class SortClauseListBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var values = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (values == ValueProviderResult.None)
        {
            bindingContext.Result = ModelBindingResult.Success(Array.Empty<SortClause>());
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(ClauseParsing.ParseSorts(values));
        return Task.CompletedTask;
    }
}
