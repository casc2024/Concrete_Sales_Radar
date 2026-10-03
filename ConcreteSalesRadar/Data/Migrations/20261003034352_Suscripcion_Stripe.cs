using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConcreteSalesRadar.Data.Migrations
{
    /// <inheritdoc />
    public partial class Suscripcion_Stripe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelacionProgramada",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "usuarios",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelacionProgramada",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "usuarios");
        }
    }
}
