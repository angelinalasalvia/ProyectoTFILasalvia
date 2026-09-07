using BLL;
using DAL;
using Servicios;
using UI.Components;
using UI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddDbContext<ChurnDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// DAL 
/*builder.Services.AddScoped<IDALCliente, DALCliente>();
builder.Services.AddScoped<IDALFactorRiesgo, DALFactorRiesgo>();
builder.Services.AddScoped<IDALModelo, DALModelo>();
builder.Services.AddScoped<IDALPrediccion, DALPrediccion>();*/
builder.Services.AddScoped<IAccesoDatos, AccesoDatos>();

// BLL
builder.Services.AddScoped<BLLPrediccion>();
builder.Services.AddScoped<BLLModelo>();
builder.Services.AddScoped<BLLCliente>();
builder.Services.AddScoped<BLLFactorRiesgo>();
builder.Services.AddScoped<BLLCampana>();
builder.Services.AddScoped<BLLHistorialAcciones>();

// UI
builder.Services.AddScoped<GeneradorPdf>();

builder.Services.AddHostedService<PrediccionBackgroundService>();

var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();