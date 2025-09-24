using Blazored.LocalStorage;
using Brism;
using Mas.Infrastructure.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddBlazoredLocalStorage();
//builder.Services.AddTransient<Allegiance.Blazor.Highcharts.Services.IChartService, Allegiance.Blazor.Highcharts.Services.ChartService>();
//builder.Services.AddTransient<MonicaBlazorUI.Services.MonicaIO>();
// builder.Services.AddTransient<MonicaBlazorUI.Services.RunMonica>();
builder.Services.AddBrism();

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

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();