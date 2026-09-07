using System.Text.Json.Serialization;
using Inandu.Grid.Extensions;
using Inandu.Grid.Extensions.AspNetCore;
using Inandu.Grid.Extensions.Playground;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.UseDefaultFiles();
app.UseStaticFiles();

// The one endpoint the whole playground is about: turn the grid's serverSide request into one page.
app.MapGet("/api/products", (HttpRequest request) =>
{
    var result = ProductStore.All.ToInanduGrid(request.QueryString.Value, options =>
    {
        options.DefaultPageSize = 25;
        options.MaxPageSize = 200;
        options.SearchableFields = new List<string> { nameof(Product.Name), nameof(Product.Sku), nameof(Product.Category) };
    });

    return Results.Ok(result);
})
.WithName("GetProducts");

static void Configure(InanduGridOptions o)
{
    o.DefaultPageSize = 25;
    o.MaxPageSize = 200;
    o.SearchableFields = new List<string> { nameof(Product.Name), nameof(Product.Sku), nameof(Product.Category) };
}

// Projection: filter/sort on the entity, return a slim DTO.
app.MapGet("/api/products/summary", (HttpRequest request) =>
        Results.Ok(ProductStore.All.ToInanduGrid(
            p => new ProductSummary(p.Id, p.Name, p.Price, p.Status),
            request.QueryString.Value,
            Configure)))
    .WithName("GetProductSummaries");

// Server-side grouping with lazy drill-down (?groupBy=category  ·  &groupKeys=Cables).
app.MapGet("/api/products/groups", (HttpRequest request) =>
    {
        var r = ProductStore.All.ToInanduGridGrouped(InanduGridOptions.FromQueryString(request.QueryString.Value, Configure));
        return r.IsLeaf ? Results.Ok(r) : Results.Ok(r);
    })
    .WithName("GetProductGroups");

// Advanced filter / any request from a JSON POST body, via the ASP.NET Core binding helper.
app.MapPost("/api/products/query", async (HttpRequest request) =>
    {
        var gridRequest = await InanduGridBinding.FromHttpRequestAsync(request);
        return Results.Ok(ProductStore.All.ToInanduGrid(InanduGridOptions.For(gridRequest, Configure)));
    })
    .WithName("QueryProducts");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", rows = ProductStore.All.Count }));

app.Run();
