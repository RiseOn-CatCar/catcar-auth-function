using CatCar.AuthFunction;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.UseMiddleware<CorrelationMiddleware>();
builder.AddServiceDefaults();

builder.Services.Configure<CustomerJwtOptions>(builder.Configuration.GetSection(CustomerJwtOptions.SectionName));
builder.Services.AddSingleton<CustomerTokenIssuer>();

var osBaseUrl = builder.Configuration["OsService:BaseUrl"];
var internalApiKey = builder.Configuration["OsService:InternalApiKey"];
if (string.IsNullOrWhiteSpace(osBaseUrl) || !Uri.TryCreate(osBaseUrl, UriKind.Absolute, out var osUri)
    || (osUri.Scheme != Uri.UriSchemeHttp && osUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException("A valid OsService:BaseUrl is required.");
}

if (string.IsNullOrWhiteSpace(internalApiKey))
{
    throw new InvalidOperationException("OsService:InternalApiKey is required.");
}

builder.Services.AddHttpClient<ICustomerLookupService, CustomerLookupService>(client =>
{
    client.BaseAddress = osUri;
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("X-Internal-Api-Key", internalApiKey);
}).RemoveAllLoggers();

var host = builder.Build();

host.Run();
