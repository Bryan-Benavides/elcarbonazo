namespace ElCarbonazo.Api.Security;

public interface IAdminBootstrapService
{
    Task BootstrapAsync(string nombre, string email, string password, CancellationToken cancellationToken);

    Task SeedDevelopmentAsync(CancellationToken cancellationToken);
}
