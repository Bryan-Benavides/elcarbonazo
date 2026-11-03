namespace ElCarbonazo.Api.DTOs;

public sealed record ActivityResponse(
    DateTimeOffset InactivityExpiresAt,
    DateTimeOffset AbsoluteSessionExpiresAt,
    DateTimeOffset ServerTime);
