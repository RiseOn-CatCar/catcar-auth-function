namespace CatCar.AuthFunction;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

public sealed class AuthenticateCustomerFunction(
    CustomerTokenIssuer tokenIssuer,
    ICustomerLookupService customerLookupService,
    IOptions<CustomerJwtOptions> options)
{
    private readonly CustomerJwtOptions _options = options.Value;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Function("AuthFunctionRoot")]
    public static HttpResponseData Root(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "")] HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Redirect);
        response.Headers.Add("Location", "/api/auth/customer");
        return response;
    }

    [Function(nameof(AuthenticateCustomerFunction))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options", Route = "auth/customer")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        // Handle CORS preflight
        if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = request.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(optionsResponse);
            return optionsResponse;
        }

        // GET only serves the interactive documentation page.
        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var htmlResponse = request.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(htmlResponse);
            htmlResponse.Headers.Add("Content-Type", "text/html; charset=utf-8");
            await htmlResponse.WriteStringAsync(GetInteractiveHtmlPage(), cancellationToken).ConfigureAwait(false);
            return htmlResponse;
        }

        // Handle POST (Standard API authentication)
        CustomerAuthenticationRequest? authenticationRequest = null;
        try
        {
            authenticationRequest = await JsonSerializer.DeserializeAsync<CustomerAuthenticationRequest>(
                request.Body,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Fallthrough to invalid document
        }

        if (authenticationRequest is null || !DocumentNumberValidator.IsValid(authenticationRequest.DocumentNumber))
        {
            return await CreateInvalidDocumentResponseAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return await CreateAuthenticationResponseAsync(
            request,
            NormalizeDocumentNumber(authenticationRequest.DocumentNumber),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseData> CreateAuthenticationResponseAsync(
        HttpRequestData request,
        string documentNumber,
        CancellationToken cancellationToken)
    {
        var customer = await customerLookupService.FindByDocumentNumberAsync(documentNumber, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return await CreateProblemResponseAsync(
                request,
                HttpStatusCode.NotFound,
                "Customer not found",
                "No customer was found for the provided document number.",
                cancellationToken).ConfigureAwait(false);
        }

        if (!customer.IsActive)
        {
            return await CreateProblemResponseAsync(
                request,
                HttpStatusCode.Forbidden,
                "Customer is inactive",
                "The customer associated with the provided document number is inactive.",
                cancellationToken).ConfigureAwait(false);
        }

        var issuedToken = tokenIssuer.Issue(customer.Id, documentNumber);
        var response = request.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response);
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
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
    }

    private Task<HttpResponseData> CreateInvalidDocumentResponseAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        CreateProblemResponseAsync(
            request,
            HttpStatusCode.BadRequest,
            "Invalid document number",
            "documentNumber must be a valid CPF or CNPJ.",
            cancellationToken);

    private async Task<HttpResponseData> CreateProblemResponseAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string title,
        string detail,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        AddCorsHeaders(response);
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

    private static string NormalizeDocumentNumber(string documentNumber) =>
        string.Concat(documentNumber.Where(char.IsDigit));

    private static string GetInteractiveHtmlPage() =>
        """
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>CatCar — Customer Authentication Service</title>
          <style>
            :root {
              --bg: #0f172a;
              --card: #1e293b;
              --primary: #8b5cf6;
              --primary-hover: #7c3aed;
              --text: #f8fafc;
              --text-muted: #94a3b8;
              --border: #334155;
              --success: #10b981;
              --error: #ef4444;
              --code-bg: #090d16;
            }
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
              font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
              background-color: var(--bg);
              color: var(--text);
              display: flex;
              justify-content: center;
              padding: 2rem 1rem;
              min-height: 100vh;
            }
            .container {
              max-width: 720px;
              width: 100%;
            }
            .header {
              text-align: center;
              margin-bottom: 2rem;
            }
            .header h1 {
              font-size: 1.875rem;
              font-weight: 700;
              margin-bottom: 0.5rem;
              background: linear-gradient(135deg, #a78bfa, #c084fc);
              -webkit-background-clip: text;
              -webkit-text-fill-color: transparent;
            }
            .badge {
              display: inline-block;
              background: rgba(139, 92, 246, 0.2);
              color: #c4b5fd;
              font-size: 0.75rem;
              font-weight: 600;
              padding: 0.25rem 0.75rem;
              border-radius: 9999px;
              border: 1px solid rgba(139, 92, 246, 0.3);
            }
            .card {
              background: var(--card);
              border: 1px solid var(--border);
              border-radius: 0.75rem;
              padding: 1.5rem;
              margin-bottom: 1.5rem;
              box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
            }
            .card-title {
              font-size: 1.125rem;
              font-weight: 600;
              margin-bottom: 1rem;
              display: flex;
              align-items: center;
              gap: 0.5rem;
            }
            label {
              display: block;
              font-size: 0.875rem;
              color: var(--text-muted);
              margin-bottom: 0.5rem;
            }
            .input-group {
              display: flex;
              gap: 0.5rem;
              margin-bottom: 1rem;
            }
            input[type="text"] {
              flex: 1;
              background: var(--code-bg);
              border: 1px solid var(--border);
              border-radius: 0.5rem;
              padding: 0.625rem 0.875rem;
              color: var(--text);
              font-family: monospace;
              font-size: 1rem;
              outline: none;
              transition: border-color 0.2s;
            }
            input[type="text"]:focus {
              border-color: var(--primary);
            }
            button {
              background: var(--primary);
              color: white;
              font-weight: 600;
              border: none;
              border-radius: 0.5rem;
              padding: 0.625rem 1.25rem;
              cursor: pointer;
              transition: background-color 0.2s, transform 0.1s;
            }
            button:hover { background: var(--primary-hover); }
            button:active { transform: scale(0.98); }
            .btn-outline {
              background: transparent;
              border: 1px solid var(--border);
              color: var(--text-muted);
            }
            .btn-outline:hover {
              background: rgba(255, 255, 255, 0.05);
              color: var(--text);
            }
            .quick-buttons {
              display: flex;
              gap: 0.5rem;
              margin-bottom: 1rem;
            }
            .quick-buttons button {
              font-size: 0.75rem;
              padding: 0.35rem 0.65rem;
            }
            .result-box {
              background: var(--code-bg);
              border: 1px solid var(--border);
              border-radius: 0.5rem;
              padding: 1rem;
              font-family: monospace;
              font-size: 0.875rem;
              white-space: pre-wrap;
              word-break: break-all;
              max-height: 250px;
              overflow-y: auto;
              color: var(--text-muted);
            }
            .result-box.success { color: var(--success); border-color: rgba(16, 185, 129, 0.4); }
            .result-box.error { color: var(--error); border-color: rgba(239, 68, 68, 0.4); }
            .token-display {
              margin-top: 1rem;
              display: none;
            }
            .token-display.visible {
              display: block;
            }
            .copy-btn {
              margin-top: 0.5rem;
              font-size: 0.75rem;
              padding: 0.35rem 0.75rem;
            }
            pre {
              background: var(--code-bg);
              border: 1px solid var(--border);
              border-radius: 0.5rem;
              padding: 1rem;
              font-family: monospace;
              font-size: 0.8125rem;
              overflow-x: auto;
              color: #e2e8f0;
            }
          </style>
        </head>
        <body>
          <div class="container">
            <div class="header">
              <h1>🚗 CatCar Auth Serverless Function</h1>
              <span class="badge">Azure Functions Isolated Worker (.NET 10)</span>
            </div>

            <div class="card">
              <div class="card-title">🔑 Teste Interativo de Autenticação</div>
              <label for="docNumber">Documento do Cliente (CPF ou CNPJ):</label>
              <div class="input-group">
                <input type="text" id="docNumber" value="52998224725" placeholder="Digite CPF ou CNPJ" />
                <button id="btnSubmit" onclick="authenticate()">Autenticar</button>
              </div>
              <div class="quick-buttons">
                <button class="btn-outline" onclick="setDoc('52998224725')">CPF Válido 1 (52998224725)</button>
                <button class="btn-outline" onclick="setDoc('04252011000110')">CNPJ Válido</button>
                <button class="btn-outline" onclick="setDoc('12345678900')">CPF Inválido</button>
              </div>

              <div id="resultBox" class="result-box">Aguardando solicitação... Clique em "Autenticar" para emitir um token JWT.</div>

              <div id="tokenSection" class="token-display">
                <label>Token Bearer Gerado:</label>
                <div id="tokenBox" class="result-box success"></div>
                <button class="btn-outline copy-btn" onclick="copyToken()">📋 Copiar Token</button>
              </div>
            </div>

            <div class="card">
              <div class="card-title">📡 Exemplo de Integração (cURL / HTTP)</div>
              <pre>curl -X POST http://localhost:7071/api/auth/customer \
          -H "Content-Type: application/json" \
          -d '{"documentNumber": "52998224725"}'</pre>
            </div>
          </div>

          <script>
            function setDoc(val) {
              document.getElementById('docNumber').value = val;
            }

            async function authenticate() {
              const doc = document.getElementById('docNumber').value.trim();
              const resultBox = document.getElementById('resultBox');
              const tokenSection = document.getElementById('tokenSection');
              const tokenBox = document.getElementById('tokenBox');
              const btn = document.getElementById('btnSubmit');

              btn.disabled = true;
              btn.textContent = 'Autenticando...';
              resultBox.className = 'result-box';
              resultBox.textContent = 'Enviando requisição POST para /api/auth/customer...';
              tokenSection.className = 'token-display';

              try {
                const response = await fetch('/api/auth/customer', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ documentNumber: doc })
                });

                const data = await response.json();
                if (response.ok) {
                  resultBox.className = 'result-box success';
                  resultBox.textContent = JSON.stringify(data, null, 2);
                  tokenBox.textContent = data.token;
                  tokenSection.className = 'token-display visible';
                } else {
                  resultBox.className = 'result-box error';
                  resultBox.textContent = 'Erro HTTP ' + response.status + ':\n' + JSON.stringify(data, null, 2);
                }
              } catch (err) {
                resultBox.className = 'result-box error';
                resultBox.textContent = 'Erro na requisição: ' + err.message;
              } finally {
                btn.disabled = false;
                btn.textContent = 'Autenticar';
              }
            }

            function copyToken() {
              const token = document.getElementById('tokenBox').textContent;
              navigator.clipboard.writeText(token);
              alert('Token JWT copiado para a área de transferência!');
            }
          </script>
        </body>
        </html>
        """;
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

public sealed record CustomerAuthenticationRequest(string DocumentNumber);

public sealed record CustomerAuthenticationResponse(string Token, int ExpiresIn, string TokenType);

public sealed record IssuedCustomerToken(string Token, int ExpiresInSeconds, Guid CustomerId);

public sealed record ProblemDetailsResponse(string Type, string Title, int Status, string Detail, string Instance);
