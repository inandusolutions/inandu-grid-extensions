using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Inandu.Grid.Extensions.AspNetCore;

/// <summary>DI helpers for wiring inandu-grid request binding into MVC.</summary>
public static class InanduGridMvcExtensions
{
    /// <summary>
    /// Registers <see cref="InanduGridModelBinderProvider"/> so any <see cref="InanduGridRequest"/>
    /// action parameter binds from the HTTP request without <see cref="FromInanduGridAttribute"/>.
    /// Call on the result of <c>AddControllers()</c> / <c>AddMvc()</c>.
    /// </summary>
    public static IMvcBuilder AddInanduGridModelBinding(this IMvcBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.AddMvcOptions(options =>
        {
            for (var i = 0; i < options.ModelBinderProviders.Count; i++)
            {
                if (options.ModelBinderProviders[i] is InanduGridModelBinderProvider)
                {
                    return;
                }
            }

            options.ModelBinderProviders.Insert(0, new InanduGridModelBinderProvider());
        });

        return builder;
    }
}
