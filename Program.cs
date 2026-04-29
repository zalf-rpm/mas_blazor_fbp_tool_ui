using BlazorDrawFBP.Services;
using BlazorDrawFBP.Shared;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

//builder.Services.AddSingleton<Mas.Infrastructure.Common.ConnectionManager>();
builder.Services.AddScoped<Mas.Infrastructure.Common.ConnectionManager>();
builder.Services.AddBlazoredLocalStorage();

//builder.Services.AddTransient<Allegiance.Blazor.Highcharts.Services.IChartService, Allegiance.Blazor.Highcharts.Services.ChartService>();
//builder.Services.AddTransient<MonicaBlazorUI.Services.MonicaIO>();
// builder.Services.AddTransient<MonicaBlazorUI.Services.RunMonica>();

builder.Services.AddScoped<CleanupDiagramService>();
builder.Services.AddScoped<CircuitHandler, AppCircuitHandler>();

// builder.Services.AddScoped<BlazorDispatcher>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAntiforgery();

app.MapRazorComponents<BlazorDrawFBP.App>()
    .AddInteractiveServerRenderMode();

app.Run();
