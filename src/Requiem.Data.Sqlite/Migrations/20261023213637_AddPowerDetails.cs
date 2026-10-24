using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Requiem.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPowerDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Powers",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionalSuccessText",
                table: "Powers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Powers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "Powers");

            migrationBuilder.DropColumn(
                name: "ExceptionalSuccessText",
                table: "Powers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Powers");
        }
    }
}
