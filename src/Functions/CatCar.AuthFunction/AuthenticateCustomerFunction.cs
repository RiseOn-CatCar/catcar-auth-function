namespace CatCar.AuthFunction;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

public sealed class AuthenticateCustomerFunction(CustomerTokenIssuer tokenIssuer)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Function(nameof(AuthenticateCustomerFunction))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/customer")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var authenticationRequest = await JsonSerializer.DeserializeAsync<CustomerAuthenticationRequest>(
            request.Body,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        if (authenticationRequest is null || !DocumentNumberValidator.IsValid(authenticationRequest.DocumentNumber))
        {
            return await CreateInvalidDocumentResponseAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var issuedToken = tokenIssuer.Issue(authenticationRequest.DocumentNumber);
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        await JsonSerializer.SerializeAsync(
            response.Body,
            new CustomerAuthenticationResponse(issuedToken.Token, issuedToken.ExpiresInSeconds, "Bearer"),
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        return response;
    }

    private static async Task<HttpResponseData> CreateInvalidDocumentResponseAsync(
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.BadRequest;
        response.Headers.Add("Content-Type", "application/problem+json; charset=utf-8");

        await JsonSerializer.SerializeAsync(
            response.Body,
            new ProblemDetailsResponse(
                "https://www.rfc-editor.org/rfc/rfc7807#section-3.1",
                "Invalid document number",
                (int)HttpStatusCode.BadRequest,
                "documentNumber must be a valid CPF or CNPJ.",
                request.Url.AbsolutePath),
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        return response;
    }
}

public sealed class CustomerTokenIssuer(IOptions<CustomerJwtOptions> options)
{
    private readonly CustomerJwtOptions _options = options.Value;

    public IssuedCustomerToken Issue(string documentNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        if (_options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("CustomerJwt:SigningKey must contain at least 32 characters.");
        }

        var issuedAtUtc = DateTime.UtcNow;
        var expiresAtUtc = issuedAtUtc.AddSeconds(_options.ExpirationSeconds);
        var customerId = CreateCustomerId(documentNumber);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, documentNumber),
            new Claim("customer_id", customerId.ToString()),
            new Claim("role", "Customer"),
        };
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            issuedAtUtc,
            expiresAtUtc,
            credentials);

        return new IssuedCustomerToken(new JwtSecurityTokenHandler().WriteToken(token), _options.ExpirationSeconds, customerId);
    }

    private static Guid CreateCustomerId(string documentNumber)
    {
        var documentHash = SHA256.HashData(Encoding.UTF8.GetBytes(documentNumber));
        return new Guid(documentHash.AsSpan(0, 16));
    }
}

public sealed class CustomerJwtOptions
{
    public const string SectionName = "CustomerJwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "CatCarAuthServer";

    public string Audience { get; set; } = "CatCarApi";

    public int ExpirationSeconds { get; set; } = 3600;
}

public static class DocumentNumberValidator
{
    public static bool IsValid(string? documentNumber)
    {
        if (string.IsNullOrWhiteSpace(documentNumber) || documentNumber.Any(static character => !char.IsAsciiDigit(character)))
        {
            return false;
        }

        return documentNumber.Length switch
        {
            11 => IsValidCpf(documentNumber),
            14 => IsValidCnpj(documentNumber),
            _ => false,
        };
    }

    private static bool IsValidCpf(string cpf)
    {
        if (HasOnlyOneDistinctDigit(cpf))
        {
            return false;
        }

        return CalculateCpfDigit(cpf, 9) == cpf[9] - '0'
            && CalculateCpfDigit(cpf, 10) == cpf[10] - '0';
    }

    private static int CalculateCpfDigit(string cpf, int digitPosition)
    {
        var sum = 0;
        for (var index = 0; index < digitPosition; index++)
        {
            sum += (cpf[index] - '0') * (digitPosition + 1 - index);
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static bool IsValidCnpj(string cnpj)
    {
        if (HasOnlyOneDistinctDigit(cnpj))
        {
            return false;
        }

        return CalculateCnpjDigit(cnpj, 12) == cnpj[12] - '0'
            && CalculateCnpjDigit(cnpj, 13) == cnpj[13] - '0';
    }

    private static int CalculateCnpjDigit(string cnpj, int digitPosition)
    {
        var sum = 0;
        var weight = digitPosition == 12 ? 5 : 6;
        for (var index = 0; index < digitPosition; index++)
        {
            sum += (cnpj[index] - '0') * weight;
            weight = weight == 2 ? 9 : weight - 1;
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static bool HasOnlyOneDistinctDigit(string documentNumber)
    {
        var firstDigit = documentNumber[0];
        for (var index = 1; index < documentNumber.Length; index++)
        {
            if (documentNumber[index] != firstDigit)
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record CustomerAuthenticationRequest(string DocumentNumber);

public sealed record CustomerAuthenticationResponse(string Token, int ExpiresIn, string TokenType);

public sealed record IssuedCustomerToken(string Token, int ExpiresInSeconds, Guid CustomerId);

public sealed record ProblemDetailsResponse(string Type, string Title, int Status, string Detail, string Instance);
