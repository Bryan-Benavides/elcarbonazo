namespace ElCarbonazo.Api.Configuration;

public sealed class PasswordHashingOptions
{
    public const string SectionName = "PasswordHashing";

    public int WorkFactor { get; set; } = 12;
}
