namespace ElCarbonazo.Api.Security;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);

    bool NeedsRehash(string passwordHash);

    void VerifyDummy(string password);
}
