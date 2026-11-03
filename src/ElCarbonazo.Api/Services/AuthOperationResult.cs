using ElCarbonazo.Api.DTOs;

namespace ElCarbonazo.Api.Services;

public enum AuthOperationOutcome
{
    Success,
    InvalidCredentials,
    InvalidRefreshToken,
    SessionExpired
}

public sealed record AuthOperationResult(
    AuthOperationOutcome Outcome,
    AuthTokenResponse? Tokens = null,
    ActivityResponse? Activity = null);
