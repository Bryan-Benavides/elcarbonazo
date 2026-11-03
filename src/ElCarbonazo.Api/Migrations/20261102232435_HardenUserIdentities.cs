using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class HardenUserIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $migration$
                DECLARE
                    collision_ids text;
                    invalid_ids text;
                BEGIN
                    IF current_setting('server_encoding') <> 'UTF8' THEN
                        RAISE EXCEPTION
                            'No se pueden normalizar identidades: la base debe usar codificación UTF8.';
                    END IF;

                    SELECT string_agg(ids, '; ' ORDER BY normalized_value)
                    INTO collision_ids
                    FROM (
                        SELECT lower(btrim(normalize(nombre, NFC))) AS normalized_value,
                               string_agg(id_usuario::text, ',' ORDER BY id_usuario) AS ids
                        FROM public.usuario
                        GROUP BY lower(btrim(normalize(nombre, NFC)))
                        HAVING COUNT(*) > 1
                    ) collisions;

                    IF collision_ids IS NOT NULL THEN
                        RAISE EXCEPTION
                            'No se pueden normalizar usuarios: nombres colisionan en IDs %.',
                            collision_ids;
                    END IF;

                    SELECT string_agg(ids, '; ' ORDER BY normalized_value)
                    INTO collision_ids
                    FROM (
                        SELECT lower(btrim(normalize(email, NFC))) AS normalized_value,
                               string_agg(id_usuario::text, ',' ORDER BY id_usuario) AS ids
                        FROM public.usuario
                        GROUP BY lower(btrim(normalize(email, NFC)))
                        HAVING COUNT(*) > 1
                    ) collisions;

                    IF collision_ids IS NOT NULL THEN
                        RAISE EXCEPTION
                            'No se pueden normalizar usuarios: emails colisionan en IDs %.',
                            collision_ids;
                    END IF;

                    SELECT string_agg(id_usuario::text, ',' ORDER BY id_usuario)
                    INTO invalid_ids
                    FROM public.usuario
                    WHERE char_length(lower(btrim(normalize(nombre, NFC)))) NOT BETWEEN 3 AND 50
                       OR lower(btrim(normalize(nombre, NFC))) !~
                          '^[[:alnum:]]+([ ._-][[:alnum:]]+)*$';

                    IF invalid_ids IS NOT NULL THEN
                        RAISE EXCEPTION
                            'No se pueden normalizar usuarios: nombres inválidos en IDs %.',
                            invalid_ids;
                    END IF;

                    SELECT string_agg(id_usuario::text, ',' ORDER BY id_usuario)
                    INTO invalid_ids
                    FROM public.usuario
                    WHERE char_length(lower(btrim(normalize(email, NFC)))) NOT BETWEEN 3 AND 254
                       OR lower(btrim(normalize(email, NFC))) !~
                          '^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$';

                    IF invalid_ids IS NOT NULL THEN
                        RAISE EXCEPTION
                            'No se pueden normalizar usuarios: emails inválidos en IDs %.',
                            invalid_ids;
                    END IF;

                    UPDATE public.usuario
                    SET nombre = lower(btrim(normalize(nombre, NFC))),
                        email = lower(btrim(normalize(email, NFC)));
                END
                $migration$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "nombre",
                table: "usuario",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "usuario",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bloqueo_hasta",
                table: "usuario",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "intentos_fallidos",
                table: "usuario",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_usuario_email",
                table: "usuario",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usuario_nombre",
                table: "usuario",
                column: "nombre",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_usuario_email_longitud",
                table: "usuario",
                sql: "char_length(email) BETWEEN 3 AND 254");

            migrationBuilder.AddCheckConstraint(
                name: "CK_usuario_email_normalizado",
                table: "usuario",
                sql: "email = normalize(email, NFC) AND email = lower(btrim(email))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_usuario_intentos_fallidos",
                table: "usuario",
                sql: "intentos_fallidos BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_usuario_nombre_longitud",
                table: "usuario",
                sql: "char_length(nombre) BETWEEN 3 AND 50");

            migrationBuilder.AddCheckConstraint(
                name: "CK_usuario_nombre_normalizado",
                table: "usuario",
                sql: "nombre = normalize(nombre, NFC) AND nombre = lower(btrim(nombre))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_usuario_email",
                table: "usuario");

            migrationBuilder.DropIndex(
                name: "IX_usuario_nombre",
                table: "usuario");

            migrationBuilder.DropCheckConstraint(
                name: "CK_usuario_email_longitud",
                table: "usuario");

            migrationBuilder.DropCheckConstraint(
                name: "CK_usuario_email_normalizado",
                table: "usuario");

            migrationBuilder.DropCheckConstraint(
                name: "CK_usuario_intentos_fallidos",
                table: "usuario");

            migrationBuilder.DropCheckConstraint(
                name: "CK_usuario_nombre_longitud",
                table: "usuario");

            migrationBuilder.DropCheckConstraint(
                name: "CK_usuario_nombre_normalizado",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "bloqueo_hasta",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "intentos_fallidos",
                table: "usuario");

            migrationBuilder.AlterColumn<string>(
                name: "nombre",
                table: "usuario",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "usuario",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);
        }
    }
}
