using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElCarbonazo.Api.Migrations;

public partial class ContinuarPedidoTrasAnulacion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "permite_pedido_tras_anulacion", table: "mesa",
            type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(
            name: "permite_pedido_tras_anulacion", table: "grupo_mesa",
            type: "boolean", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new InvalidOperationException("La reversión automática descartaría el estado de continuidad.");
}
