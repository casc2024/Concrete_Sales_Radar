using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConcreteSalesRadar.Data.Migrations
{
    /// <inheritdoc />
    public partial class Proyecto_CalidadDato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfidenceScore",
                table: "proyectos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EstadoVerificacion",
                table: "proyectos",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Estimado"); // filas previas: estado mínimo válido

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaVerificacion",
                table: "proyectos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<bool>(
                name: "YardasReportadas",
                table: "proyectos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfidenceScore",
                table: "proyectos");

            migrationBuilder.DropColumn(
                name: "EstadoVerificacion",
                table: "proyectos");

            migrationBuilder.DropColumn(
                name: "UltimaVerificacion",
                table: "proyectos");

            migrationBuilder.DropColumn(
                name: "YardasReportadas",
                table: "proyectos");
        }
    }
}
