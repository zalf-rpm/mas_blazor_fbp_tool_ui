using BlazorDrawFBP.Services;
using BlazorDrawFBP.Shared;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Server.Circuits;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddScoped<Mas.Infrastructure.Common.ConnectionManager>();
builder.Services.AddBlazoredLocalStorage();

builder.Services.AddScoped<CleanupDiagramService>();
builder.Services.AddScoped<CircuitHandler, AppCircuitHandler>();
builder.Services.AddScoped<IFbpRuntimeService, FbpRuntimeService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
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
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<BlazorDrawFBP.App>().AddInteractiveServerRenderMode();

app.Run();
