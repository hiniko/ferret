using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ferret.AspNetCore.Binding;

/// <summary>
/// Binds repeated query values into <see cref="IReadOnlyList{String}"/>. MVC's built-in
/// collection binder cannot materialise a read-only interface into an init-only property,
/// so it silently leaves the default empty list in place.
/// Matches <c>QueryStringQueryBinder</c>: repeated keys only, no comma splitting.
/// </summary>
public sealed class StringListBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var values = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (values == ValueProviderResult.None)
        {
            bindingContext.Result = ModelBindingResult.Success(Array.Empty<string>());
            return Task.CompletedTask;
        }

        var parsed = values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToArray();

        bindingContext.Result = ModelBindingResult.Success(parsed);
        return Task.CompletedTask;
    }
}
