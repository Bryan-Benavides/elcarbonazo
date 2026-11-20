using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElCarbonazo.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261103050000_EnforceAuditAppendOnly")]
public sealed class EnforceAuditAppendOnly : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TRIGGER TR_evento_auditoria_no_truncate
            BEFORE TRUNCATE ON public.evento_auditoria
            FOR EACH STATEMENT EXECUTE FUNCTION public.reject_evento_auditoria_mutation();

            REVOKE UPDATE, DELETE, TRUNCATE ON public.evento_auditoria FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "La protección append-only de la auditoría no admite rollback por riesgo de pérdida del historial.");
    }
}
