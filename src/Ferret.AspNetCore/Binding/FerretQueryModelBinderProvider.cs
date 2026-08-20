using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ferret.AspNetCore.Binding;

/// <summary>
/// Binds <see cref="OffsetApiQuery"/> and <see cref="CursorApiQuery"/> action parameters
/// straight from the query string, bypassing MVC's complex-object binder.
/// </summary>
/// <remarks>
/// MVC prefixes complex-object binding with the action parameter name. An action written
/// as <c>Get([FromQuery] OffsetApiQuery q)</c> therefore makes MVC look for <c>q.Q</c>,
/// <c>q.limit</c>, … as soon as the request carries a <c>?q=</c> key — and every property
/// silently falls back to its default. Binding the whole type in one step removes the
/// dependency on what the parameter happens to be called.
/// </remarks>
public sealed class FerretQueryModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Metadata.ModelType == typeof(OffsetApiQuery))
            return new FerretQueryModelBinder(static q => QueryStringQueryBinder.BindOffset(q));

        if (context.Metadata.ModelType == typeof(CursorApiQuery))
            return new FerretQueryModelBinder(static q => QueryStringQueryBinder.BindCursor(q));

        return null;
    }
}

internal sealed class FerretQueryModelBinder : IModelBinder
{
    private readonly Func<IQueryCollection, object> _bind;

    public FerretQueryModelBinder(Func<IQueryCollection, object> bind) => _bind = bind;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        bindingContext.Result = ModelBindingResult.Success(
            _bind(bindingContext.HttpContext.Request.Query));
        return Task.CompletedTask;
    }
}
