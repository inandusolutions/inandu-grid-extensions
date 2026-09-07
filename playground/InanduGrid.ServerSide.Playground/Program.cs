using System.Text.Json.Serialization;
using InanduGrid.ServerSide;
using InanduGrid.ServerSide.Playground;

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

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", rows = ProductStore.All.Count }));

app.Run();
