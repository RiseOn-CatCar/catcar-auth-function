namespace CatCar.AuthFunction.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

public sealed class AuthenticateCustomerFunctionTests
{
    private const string SigningKey = "test-signing-key-with-at-least-thirty-two-characters";
    private readonly CustomerTokenIssuer _tokenIssuer = new(Options.Create(new CustomerJwtOptions { SigningKey = SigningKey }));

    [Fact]
    public void Issue_WithValidCpf_IssuesBearerTokenForOneHour()
    {
        // Arrange
        const string cpf = "52998224725";

        // Act
        var issuedToken = _tokenIssuer.Issue(cpf);

        // Assert
        issuedToken.Token.Should().NotBeNullOrWhiteSpace();
        issuedToken.ExpiresInSeconds.Should().Be(3600);
        issuedToken.CustomerId.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("12345678900")]
    [InlineData("04252011000111")]
    [InlineData("11111111111")]
    [InlineData("not-a-document")]
    public void IsValid_WithInvalidCpfOrCnpj_ReturnsFalse(string documentNumber)
    {
        DocumentNumberValidator.IsValid(documentNumber).Should().BeFalse();
    }

    [Theory]
    [InlineData("52998224725")]
    [InlineData("04252011000110")]
    public void IsValid_WithCpfOrCnpjUsingCorrectCheckDigits_ReturnsTrue(string documentNumber)
    {
        DocumentNumberValidator.IsValid(documentNumber).Should().BeTrue();
    }

    [Fact]
    public void Issue_EmbedsRequiredClaimsInSignedToken()
    {
        // Arrange
        const string cpf = "52998224725";

        // Act
        var issuedToken = _tokenIssuer.Issue(cpf);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(
            issuedToken.Token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "CatCarAuthServer",
                ValidateAudience = true,
                ValidAudience = "CatCarApi",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ClockSkew = TimeSpan.Zero,
            },
            out var validatedToken);

        // Assert
        validatedToken.Should().BeOfType<JwtSecurityToken>();
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(cpf);
        principal.FindFirst("customer_id")?.Value.Should().Be(issuedToken.CustomerId.ToString());
        principal.FindFirst("role")?.Value.Should().Be("Customer");
        validatedToken.ValidTo.Should().BeAfter(DateTime.UtcNow);
    }
}
