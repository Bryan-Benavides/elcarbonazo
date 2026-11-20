using ElCarbonazo.Api.Entities;

namespace ElCarbonazo.Api.Security;

public interface IJwtTokenGenerator
{
    string Generate(Usuario usuario, Guid sessionId);
}
