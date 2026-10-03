using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ConcreteSalesRadar.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pagos_Stripe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MembresiaPagada",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "usuarios",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pagos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    MembresiaId = table.Column<int>(type: "integer", nullable: false),
                    StripeSessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodoInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodoFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pagos_membresias_MembresiaId",
                        column: x => x.MembresiaId,
                        principalTable: "membresias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pagos_MembresiaId",
                table: "pagos",
                column: "MembresiaId");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_StripeSessionId",
                table: "pagos",
                column: "StripeSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_UsuarioId",
                table: "pagos",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pagos");

            migrationBuilder.DropColumn(
                name: "MembresiaPagada",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "usuarios");
        }
    }
}
