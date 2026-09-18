namespace CatCar.AuthFunction;

using Npgsql;

public interface ICustomerLookupService
{
    Task<CustomerLookupResult?> FindByDocumentNumberAsync(string documentNumber, CancellationToken cancellationToken);
}

public sealed class CustomerLookupService(NpgsqlDataSource dataSource) : ICustomerLookupService
{
    private const string FindCustomerByDocumentQuery = """
        SELECT id, is_active
        FROM service_operations.customers
        WHERE document_number = @documentNumber
        LIMIT 1;
        """;

    public async Task<CustomerLookupResult?> FindByDocumentNumberAsync(
        string documentNumber,
        CancellationToken cancellationToken)
    {
        using var command = dataSource.CreateCommand(FindCustomerByDocumentQuery);
        command.Parameters.AddWithValue("documentNumber", documentNumber);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new CustomerLookupResult(reader.GetGuid(0), reader.GetBoolean(1))
            : null;
    }
}

public sealed record CustomerLookupResult(Guid Id, bool IsActive);
