using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Requiem.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Relationships",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Other"); // existing relationships are none of the three starting ones

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "Characters",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConceptDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    WillpowerTrigger = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourcePage = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConceptDefinitions_Name",
                table: "ConceptDefinitions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConceptDefinitions");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Relationships");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "Characters");
        }
    }
}
