using ConcreteSalesRadar.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConcreteSalesRadar.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261014040000_Membresia_CamposVisibles")]
    public partial class Membresia_CamposVisibles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CamposVisibles",
                table: "membresias",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CamposVisibles",
                table: "membresias");
        }
    }
}
