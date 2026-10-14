using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConcreteSalesRadar.Data.Migrations
{
    /// <inheritdoc />
    public partial class Membresia_StripeIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripePriceId",
                table: "membresias",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeProductId",
                table: "membresias",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripePriceId",
                table: "membresias");

            migrationBuilder.DropColumn(
                name: "StripeProductId",
                table: "membresias");
        }
    }
}
