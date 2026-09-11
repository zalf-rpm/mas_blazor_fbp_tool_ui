using BlazorDrawFBP;
using BlazorDrawFBP.Services;
using Blazored.LocalStorage;
using Mas.Infrastructure.Common;
using Microsoft.AspNetCore.Components.Server.Circuits;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = false;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 3000;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.ShowTransitionDuration = 200;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
});

builder.Services.AddScoped<ConnectionManager>();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<IAppThemeService, AppThemeService>();

builder.Services.AddScoped<CleanupDiagramService>();
builder.Services.AddScoped<CircuitHandler, AppCircuitHandler>();
builder.Services.AddScoped<IFbpRuntimeService, FbpRuntimeService>();
builder.Services.AddScoped<IFbpNodeFactory, FbpNodeFactory>();
builder.Services.AddScoped<IFlowDocumentService, FlowDocumentService>();
builder.Services.AddScoped<IFbpDiagramFactory, FbpDiagramFactory>();
builder.Services.AddSingleton<IFlowSessionStore, FlowSessionStore>();
builder.Services.AddHostedService<FlowSessionReaper>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// In container environments / reverse-proxy setups (like Kubernetes), TLS termination is handled by the Ingress/Gateway.
// Only use in-process HTTPS redirection when running outside containers (e.g. local dotnet run with HTTPS dev cert).
if (
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"))
    || app.Configuration["HTTPS_PORT"] != null
)
    app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
