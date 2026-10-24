using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Requiem.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FromDots",
                table: "XpEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordedByUserId",
                table: "XpEntries",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToDots",
                table: "XpEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Trait",
                table: "XpEntries",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "Scenes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "Scenes",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DomitorId",
                table: "Characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DomitorName",
                table: "Characters",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPlayerCharacter",
                table: "Characters",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublicDescription",
                table: "Characters",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Characters",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "Characters",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Hidden");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UndoData = table.Column<string>(type: "TEXT", nullable: true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    UndoneAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ChronicleTenet = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CoterieScript = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AllowedTemplates = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchivedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CharacterLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    WithCharacterIds = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterLinks_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UsesLeft = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignInvites", x => x.Id);
                    table.CheckConstraint("CK_CampaignInvite_UsesLeft", "UsesLeft IS NULL OR UsesLeft >= 0");
                    table.ForeignKey(
                        name: "FK_CampaignInvites_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignMembers",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    JoinedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignMembers", x => new { x.CampaignId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CampaignMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignMembers_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_CampaignId",
                table: "Scenes",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_OwnerUserId",
                table: "Scenes",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_DomitorId",
                table: "Characters",
                column: "DomitorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_CampaignId_At",
                table: "AuditEntries",
                columns: new[] { "CampaignId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_CharacterId_At",
                table: "AuditEntries",
                columns: new[] { "CharacterId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignInvites_CampaignId",
                table: "CampaignInvites",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignInvites_TokenHash",
                table: "CampaignInvites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignMembers_UserId",
                table: "CampaignMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterLinks_CharacterId_At",
                table: "CharacterLinks",
                columns: new[] { "CharacterId", "At" });

            migrationBuilder.AddForeignKey(
                name: "FK_Characters_Campaigns_CampaignId",
                table: "Characters",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Characters_Characters_DomitorId",
                table: "Characters",
                column: "DomitorId",
                principalTable: "Characters",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Scenes_Campaigns_CampaignId",
                table: "Scenes",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Characters_Campaigns_CampaignId",
                table: "Characters");

            migrationBuilder.DropForeignKey(
                name: "FK_Characters_Characters_DomitorId",
                table: "Characters");

            migrationBuilder.DropForeignKey(
                name: "FK_Scenes_Campaigns_CampaignId",
                table: "Scenes");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "CampaignInvites");

            migrationBuilder.DropTable(
                name: "CampaignMembers");

            migrationBuilder.DropTable(
                name: "CharacterLinks");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Scenes_CampaignId",
                table: "Scenes");

            migrationBuilder.DropIndex(
                name: "IX_Scenes_OwnerUserId",
                table: "Scenes");

            migrationBuilder.DropIndex(
                name: "IX_Characters_DomitorId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "FromDots",
                table: "XpEntries");

            migrationBuilder.DropColumn(
                name: "RecordedByUserId",
                table: "XpEntries");

            migrationBuilder.DropColumn(
                name: "ToDots",
                table: "XpEntries");

            migrationBuilder.DropColumn(
                name: "Trait",
                table: "XpEntries");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "Scenes");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Scenes");

            migrationBuilder.DropColumn(
                name: "DomitorId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "DomitorName",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "IsPlayerCharacter",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "PublicDescription",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Characters");
        }
    }
}
