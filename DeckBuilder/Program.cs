using DeckBuilder.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(_ => Galaxy.Core.Cards.CardCatalog.LoadDefault());
builder.Services.AddSingleton(sp => new Galaxy.Core.Cards.PremadeDecks(sp.GetRequiredService<Galaxy.Core.Cards.CardCatalog>()));
builder.Services.AddScoped<DeckBuilder.DeckSession>();

var app = builder.Build();

// Optional URL prefix when hosted under a sub-path (e.g. behind a reverse proxy / tunnel at /deck).
// Requests without the prefix still work, so local access at / is unaffected.
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
{
    app.UsePathBase(pathBase);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
