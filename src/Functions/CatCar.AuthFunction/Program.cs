using CatCar.AuthFunction;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.AddServiceDefaults();

builder.Services.Configure<CustomerJwtOptions>(builder.Configuration.GetSection(CustomerJwtOptions.SectionName));
builder.Services.AddSingleton<CustomerTokenIssuer>();

var host = builder.Build();

host.Run();
