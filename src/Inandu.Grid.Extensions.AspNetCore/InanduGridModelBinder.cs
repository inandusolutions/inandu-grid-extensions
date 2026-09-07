using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Inandu.Grid.Extensions.AspNetCore;

/// <summary>
/// Put on an <see cref="InanduGridRequest"/> action parameter to bind it from the HTTP request
/// (query string, or a JSON body for a POST): <c>public IActionResult Get([FromInanduGrid] InanduGridRequest req)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromInanduGridAttribute : ModelBinderAttribute
{
    /// <summary>Creates the attribute, wiring up <see cref="InanduGridModelBinder"/>.</summary>
    public FromInanduGridAttribute()
        : base(typeof(InanduGridModelBinder))
    {
        BindingSource = BindingSource.Custom;
    }
}

/// <summary>The <see cref="IModelBinder"/> behind <see cref="FromInanduGridAttribute"/> — see <see cref="InanduGridBinding"/>.</summary>
public sealed class InanduGridModelBinder : IModelBinder
{
    /// <inheritdoc />
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext is null)
        {
            throw new ArgumentNullException(nameof(bindingContext));
        }

        var request = await InanduGridBinding
            .FromHttpRequestAsync(bindingContext.HttpContext.Request, bindingContext.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        bindingContext.Result = ModelBindingResult.Success(request);
    }
}

/// <summary>
/// An <see cref="IModelBinderProvider"/> that binds every <see cref="InanduGridRequest"/> action
/// parameter with <see cref="InanduGridModelBinder"/> — no <see cref="FromInanduGridAttribute"/>
/// needed. Register it with <see cref="InanduGridMvcExtensions.AddInanduGridModelBinding"/>.
/// </summary>
public sealed class InanduGridModelBinderProvider : IModelBinderProvider
{
    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return context.Metadata.ModelType == typeof(InanduGridRequest)
            ? new InanduGridModelBinder()
            : null;
    }
}
