namespace CatCar.AuthFunction;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

public sealed partial class AuthenticateCustomerFunction(
    CustomerTokenIssuer tokenIssuer,
    ICustomerLookupService customerLookupService,
    IOptions<CustomerJwtOptions> options,
    ILogger<AuthenticateCustomerFunction> logger)
{
    private readonly CustomerJwtOptions _options = options.Value;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Function("AuthFunctionRoot")]
    public static HttpResponseData Root(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "")] HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Redirect);
        response.Headers.Add("Location", "/api/swagger/ui");
        return response;
    }

    [Function("CustomerAuthenticationPreflight")]
    public HttpResponseData Preflight(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "auth/customer")] HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.NoContent);
        AddCorsHeaders(response);
        AddCorrelationHeader(response, GetCorrelationId(request).ToString());
        return response;
    }

    [Function("AuthenticateCustomer")]
    [OpenApiOperation(operationId: "AuthenticateCustomer", tags: new[] { "Customer Authentication" }, Summary = "Authenticate an active customer", Description = "Validates a CPF or CNPJ and returns a CatCar customer JWT.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CustomerAuthenticationRequest), Required = true, Description = "Customer document used for authentication.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(CustomerAuthenticationResponse), Summary = "Customer authenticated")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/problem+json", bodyType: typeof(ProblemDetailsResponse), Summary = "Invalid document")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/problem+json", bodyType: typeof(ProblemDetailsResponse), Summary = "Customer inactive")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/problem+json", bodyType: typeof(ProblemDetailsResponse), Summary = "Customer not found")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/customer")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var correlationId = GetCorrelationId(request);
        var correlationIdValue = correlationId.ToString();
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationIdValue
        });
        System.Diagnostics.Activity.Current?.SetTag("catcar.correlation_id", correlationIdValue);

        // Enforce APIM origin gateway key check in production for POST requests
        var expectedGatewayKey = Environment.GetEnvironmentVariable("Gateway__BackendKey");
        if (!string.IsNullOrEmpty(expectedGatewayKey) && request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.Headers.TryGetValues("X-CatCar-Gateway-Key", out var headerValues) ||
                !headerValues.Any(v => string.Equals(v, expectedGatewayKey, StringComparison.Ordinal)))
            {
                return await CreateProblemResponseAsync(
                    request,
                    correlationIdValue,
                    HttpStatusCode.Forbidden,
                    "Gateway key rejected",
                    "The request origin is not authorized.",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        CustomerAuthenticationRequest? authenticationRequest = null;
        try
        {
            authenticationRequest = await JsonSerializer.DeserializeAsync<CustomerAuthenticationRequest>(
                request.Body,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Fall through to the same response as an invalid document.
        }

        if (authenticationRequest is null || !DocumentNumberValidator.IsValid(authenticationRequest.DocumentNumber))
        {
            LogInvalidDocument();
            return await CreateInvalidDocumentResponseAsync(request, correlationIdValue, cancellationToken).ConfigureAwait(false);
        }

        return await CreateAuthenticationResponseAsync(
            request,
            NormalizeDocumentNumber(authenticationRequest.DocumentNumber),
            correlationIdValue,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseData> CreateAuthenticationResponseAsync(
        HttpRequestData request,
        string documentNumber,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var customer = await customerLookupService.FindByDocumentNumberAsync(documentNumber, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            LogCustomerNotFound();
            return await CreateProblemResponseAsync(
                request,
                correlationId,
                HttpStatusCode.NotFound,
                "Customer not found",
                "No customer was found for the provided document number.",
                cancellationToken).ConfigureAwait(false);
        }

        if (!customer.IsActive)
        {
            LogInactiveCustomer(customer.Id);
            return await CreateProblemResponseAsync(
                request,
                correlationId,
                HttpStatusCode.Forbidden,
                "Customer is inactive",
                "The customer associated with the provided document number is inactive.",
                cancellationToken).ConfigureAwait(false);
        }

        var issuedToken = tokenIssuer.Issue(customer.Id, documentNumber);
        LogCustomerAuthenticated(customer.Id);
        var response = request.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response);
        AddCorrelationHeader(response, correlationId);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        await JsonSerializer.SerializeAsync(
            response.Body,
            new CustomerAuthenticationResponse(issuedToken.Token, issuedToken.ExpiresInSeconds, "Bearer"),
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        return response;
    }

    private void AddCorsHeaders(HttpResponseData response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", _options.CorsOrigin);
        response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Correlation-Id, X-CatCar-Gateway-Key");
    }

    private Task<HttpResponseData> CreateInvalidDocumentResponseAsync(
        HttpRequestData request,
        string correlationId,
        CancellationToken cancellationToken) =>
        CreateProblemResponseAsync(
            request,
            correlationId,
            HttpStatusCode.BadRequest,
            "Invalid document number",
            "documentNumber must be a valid CPF or CNPJ.",
            cancellationToken);

    private async Task<HttpResponseData> CreateProblemResponseAsync(
        HttpRequestData request,
        string correlationId,
        HttpStatusCode statusCode,
        string title,
        string detail,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        AddCorsHeaders(response);
        AddCorrelationHeader(response, correlationId);
        response.Headers.Add("Content-Type", "application/problem+json; charset=utf-8");

        await JsonSerializer.SerializeAsync(
            response.Body,
            new ProblemDetailsResponse(
                "https://www.rfc-editor.org/rfc/rfc7807#section-3.1",
                title,
                (int)statusCode,
                detail,
                request.Url.AbsolutePath),
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        return response;
    }

    private static Guid GetCorrelationId(HttpRequestData request)
    {
        var header = request.Headers.TryGetValues("X-Correlation-Id", out var values)
            ? values.FirstOrDefault()
            : null;
        return Guid.TryParse(header, out var correlationId)
            ? correlationId
            : Guid.CreateVersion7();
    }

    private static void AddCorrelationHeader(HttpResponseData response, string correlationId) =>
        response.Headers.Add("X-Correlation-Id", correlationId);

    private static string NormalizeDocumentNumber(string documentNumber) =>
        string.Concat(documentNumber.Where(char.IsDigit));

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Customer authentication rejected because the document is invalid.")]
    partial void LogInvalidDocument();

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Customer authentication failed because the customer was not found.")]
    partial void LogCustomerNotFound();

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Customer authentication rejected because customer {CustomerId} is inactive.")]
    partial void LogInactiveCustomer(Guid customerId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Customer {CustomerId} authenticated successfully.")]
    partial void LogCustomerAuthenticated(Guid customerId);
}

public sealed class CustomerTokenIssuer(IOptions<CustomerJwtOptions> options)
{
    private readonly CustomerJwtOptions _options = options.Value;

    public IssuedCustomerToken Issue(Guid customerId, string documentNumber)
    {
        if (_options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("CustomerJwt:SigningKey must contain at least 32 characters.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expirationMinutes = _options.ExpirationMinutes > 0 ? _options.ExpirationMinutes : 60;
        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, documentNumber),
            new Claim("customer_id", customerId.ToString()),
            new Claim("role", "Customer"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new IssuedCustomerToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            (int)TimeSpan.FromMinutes(expirationMinutes).TotalSeconds,
            customerId);
    }
}

public sealed class CustomerJwtOptions
{
    public const string SectionName = "CustomerJwt";

    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "CatCar";
    public string Audience { get; set; } = "CatCar.Api";
    public int ExpirationMinutes { get; set; } = 60;
    public string CorsOrigin { get; set; } = "https://localhost:7071";
}

public static class DocumentNumberValidator
{
    public static bool IsValid(string? documentNumber)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            return false;
        }

        if (documentNumber.Any(static character => !char.IsDigit(character) && character is not '.' and not '-' and not '/'))
        {
            return false;
        }

        var digitsOnly = new string(documentNumber.Where(char.IsDigit).ToArray());
        return digitsOnly.Length switch
        {
            11 => IsValidCpf(digitsOnly),
            14 => IsValidCnpj(digitsOnly),
            _ => false
        };
    }

    private static bool IsValidCpf(string cpf)
    {
        if (cpf.Distinct().Count() == 1)
        {
            return false;
        }

        int[] multiplier1 = [10, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] multiplier2 = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2];

        var tempCpf = cpf[..9];
        var sum = 0;

        for (var i = 0; i < 9; i++)
        {
            sum += (tempCpf[i] - '0') * multiplier1[i];
        }

        var remainder = sum % 11;
        var digit1 = remainder < 2 ? 0 : 11 - remainder;

        tempCpf += digit1;
        sum = 0;

        for (var i = 0; i < 10; i++)
        {
            sum += (tempCpf[i] - '0') * multiplier2[i];
        }

        remainder = sum % 11;
        var digit2 = remainder < 2 ? 0 : 11 - remainder;

        return cpf.EndsWith($"{digit1}{digit2}", StringComparison.Ordinal);
    }

    private static bool IsValidCnpj(string cnpj)
    {
        if (cnpj.Distinct().Count() == 1)
        {
            return false;
        }

        int[] multiplier1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] multiplier2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        var tempCnpj = cnpj[..12];
        var sum = 0;

        for (var i = 0; i < 12; i++)
        {
            sum += (tempCnpj[i] - '0') * multiplier1[i];
        }

        var remainder = sum % 11;
        var digit1 = remainder < 2 ? 0 : 11 - remainder;

        tempCnpj += digit1;
        sum = 0;

        for (var i = 0; i < 13; i++)
        {
            sum += (tempCnpj[i] - '0') * multiplier2[i];
        }

        remainder = sum % 11;
        var digit2 = remainder < 2 ? 0 : 11 - remainder;

        return cnpj.EndsWith($"{digit1}{digit2}", StringComparison.Ordinal);
    }
}

public sealed record CustomerAuthenticationRequest([property: Required] string DocumentNumber);

public sealed record CustomerAuthenticationResponse(string Token, int ExpiresIn, string TokenType);

public sealed record IssuedCustomerToken(string Token, int ExpiresInSeconds, Guid CustomerId);

public sealed record ProblemDetailsResponse(string Type, string Title, int Status, string Detail, string Instance);
