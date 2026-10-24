using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Requiem.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCompelAndEdgeCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MeritDefinitions_Name_Kind",
                table: "MeritDefinitions");

            migrationBuilder.DropColumn(
                name: "IsVampireOnly",
                table: "MeritDefinitions");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "MeritDefinitions",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CompelDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Roll = table.Column<int>(type: "INTEGER", nullable: true),
                    Rating = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EndsWhen = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourcePage = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompelDefinitions", x => x.Id);
                    table.CheckConstraint("CK_CompelDefinition_Roll", "Roll IS NULL OR Roll BETWEEN 1 AND 10");
                });

            migrationBuilder.CreateTable(
                name: "EdgeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Edge = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePage = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeritDefinitions_Name_Kind_Source",
                table: "MeritDefinitions",
                columns: new[] { "Name", "Kind", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompelDefinitions_Name",
                table: "CompelDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompelDefinitions_Roll",
                table: "CompelDefinitions",
                column: "Roll",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EdgeDefinitions_Name",
                table: "EdgeDefinitions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompelDefinitions");

            migrationBuilder.DropTable(
                name: "EdgeDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_MeritDefinitions_Name_Kind_Source",
                table: "MeritDefinitions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "MeritDefinitions");

            migrationBuilder.AddColumn<bool>(
                name: "IsVampireOnly",
                table: "MeritDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_MeritDefinitions_Name_Kind",
                table: "MeritDefinitions",
                columns: new[] { "Name", "Kind" },
                unique: true);
        }
    }
}
