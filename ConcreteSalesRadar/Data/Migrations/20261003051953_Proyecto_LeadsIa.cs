using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConcreteSalesRadar.Data.Migrations
{
    /// <inheritdoc />
    public partial class Proyecto_LeadsIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaveExterna",
                table: "proyectos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GeneradoPorIa",
                table: "proyectos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_proyectos_ClaveExterna",
                table: "proyectos",
                column: "ClaveExterna");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_proyectos_ClaveExterna",
                table: "proyectos");

            migrationBuilder.DropColumn(
                name: "ClaveExterna",
                table: "proyectos");

            migrationBuilder.DropColumn(
                name: "GeneradoPorIa",
                table: "proyectos");
        }
    }
}
