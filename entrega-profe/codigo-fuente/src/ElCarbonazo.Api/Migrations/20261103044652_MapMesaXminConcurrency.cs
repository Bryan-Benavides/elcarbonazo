using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class MapMesaXminConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF maps Mesa.Version to PostgreSQL's built-in xmin system column.
            // xmin already exists for every PostgreSQL table and must not be added as DDL.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No physical schema object was added by this model-mapping migration.
        }
    }
}
