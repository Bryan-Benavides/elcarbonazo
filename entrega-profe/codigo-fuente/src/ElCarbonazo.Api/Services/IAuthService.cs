using ElCarbonazo.Api.DTOs;

namespace ElCarbonazo.Api.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthOperationResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);

    Task<AuthOperationResult> RecordActivityAsync(Guid sessionId, CancellationToken cancellationToken);

    Task LogoutAsync(Guid sessionId, CancellationToken cancellationToken);
}
