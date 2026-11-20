using System.Text;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CajaRecoveryJournalTests
{
    [Fact]
    public void Existing_empty_journal_requires_matching_installation_and_explicit_null_fields()
    {
        var installation = Guid.NewGuid();
        var json = Bytes($$"""{"version":1,"instalacion":"{{installation}}","estado":"SinPendientes","operacion":null,"propietario":null,"tipo":null}""");
        Assert.Equal("SinPendientes", CajaRecoveryJournal.Parse(json, installation)!.Estado);
        Assert.Null(CajaRecoveryJournal.Parse(json, Guid.NewGuid()));
        Assert.Null(CajaRecoveryJournal.Parse(Bytes("{}"), installation));
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("TerminalPorConciliar")]
    public void Pending_and_terminal_preserve_identity(string state)
    {
        var installation = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var json = Bytes($$"""{"version":1,"instalacion":"{{installation}}","estado":"{{state}}","operacion":"{{operation}}","propietario":7,"tipo":"CobrarCuenta"}""");
        var result = CajaRecoveryJournal.Parse(json, installation);
        Assert.Equal(operation, result!.Operacion);
        Assert.Equal(7, result.Propietario);
    }

    [Fact]
    public void Rejects_corruption_unknown_duplicate_or_sensitive_fields()
    {
        var installation = Guid.NewGuid();
        Assert.Null(CajaRecoveryJournal.Parse(Bytes("not json"), installation));
        var json = $$"""{"version":1,"version":1,"instalacion":"{{installation}}","estado":"SinPendientes","operacion":null,"propietario":null,"tipo":null}""";
        Assert.Null(CajaRecoveryJournal.Parse(Bytes(json), installation));
        json = $$"""{"version":1,"instalacion":"{{installation}}","estado":"SinPendientes","operacion":null,"propietario":null,"tipo":null,"token":"secret"}""";
        Assert.Null(CajaRecoveryJournal.Parse(Bytes(json), installation));
        Assert.Null(CajaRecoveryJournal.Parse(new byte[4097], installation));
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
