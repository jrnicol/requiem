using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Requiem.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bloodlines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WeaknessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WeaknessText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompelText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourcePage = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bloodlines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeritDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Cost = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MinDots = table.Column<int>(type: "int", nullable: true),
                    MaxDots = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Restrictions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsVampireOnly = table.Column<bool>(type: "bit", nullable: false),
                    SourcePage = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeritDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PredatorTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Restrictions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourcePage = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredatorTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PowerGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BloodlineId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourcePage = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PowerGroups_Bloodlines_BloodlineId",
                        column: x => x.BloodlineId,
                        principalTable: "Bloodlines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Concept = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Virtue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VirtueDetail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Vice = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ViceDetail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Ambition = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Faction = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PortraitUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GmNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Physical = table.Column<int>(type: "int", nullable: true),
                    Mental = table.Column<int>(type: "int", nullable: true),
                    Social = table.Column<int>(type: "int", nullable: true),
                    Edges = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<int>(type: "int", nullable: false),
                    WillpowerDots = table.Column<int>(type: "int", nullable: true),
                    WillpowerCurrent = table.Column<int>(type: "int", nullable: true),
                    Humanity = table.Column<int>(type: "int", nullable: true),
                    Morality = table.Column<int>(type: "int", nullable: true),
                    BloodlineId = table.Column<int>(type: "int", nullable: true),
                    PredatorTypeId = table.Column<int>(type: "int", nullable: true),
                    Generation = table.Column<int>(type: "int", nullable: true),
                    BloodPotency = table.Column<int>(type: "int", nullable: true),
                    VitaeCurrent = table.Column<int>(type: "int", nullable: true),
                    IsThinblood = table.Column<bool>(type: "bit", nullable: false),
                    BeastDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SireId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PotenceBoost = table.Column<int>(type: "int", nullable: false),
                    CelerityBoost = table.Column<int>(type: "int", nullable: false),
                    FortitudeBoost = table.Column<int>(type: "int", nullable: false),
                    IsInTorpor = table.Column<bool>(type: "bit", nullable: false),
                    IsStaked = table.Column<bool>(type: "bit", nullable: false),
                    IsMasquerading = table.Column<bool>(type: "bit", nullable: false),
                    IsUnderDominion = table.Column<bool>(type: "bit", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.CheckConstraint("CK_Character_BloodPotency", "BloodPotency IS NULL OR BloodPotency BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_Character_Generation", "Generation IS NULL OR Generation BETWEEN 1 AND 13");
                    table.CheckConstraint("CK_Character_Humanity", "Humanity IS NULL OR Humanity BETWEEN 0 AND 10");
                    table.CheckConstraint("CK_Character_Mental", "Mental IS NULL OR Mental BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Character_Physical", "Physical IS NULL OR Physical BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Character_Social", "Social IS NULL OR Social BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Character_WillpowerDots", "WillpowerDots IS NULL OR WillpowerDots BETWEEN 0 AND 10");
                    table.ForeignKey(
                        name: "FK_Characters_Bloodlines_BloodlineId",
                        column: x => x.BloodlineId,
                        principalTable: "Bloodlines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Characters_Characters_SireId",
                        column: x => x.SireId,
                        principalTable: "Characters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Characters_PredatorTypes_PredatorTypeId",
                        column: x => x.PredatorTypeId,
                        principalTable: "PredatorTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Powers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PowerGroupId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Flavor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VitaeCost = table.Column<int>(type: "int", nullable: true),
                    WillpowerCost = table.Column<int>(type: "int", nullable: true),
                    CostText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DicePoolText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DicePoolAttribute = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DicePoolSkill = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DicePoolPowerGroupId = table.Column<int>(type: "int", nullable: true),
                    OpposedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActionText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Duration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CastingDuration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HowToLearn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DramaticFailureText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SuccessText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourcePage = table.Column<int>(type: "int", nullable: true),
                    IsReviewed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Powers", x => x.Id);
                    table.CheckConstraint("CK_Power_Level", "Level IS NULL OR Level BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_Powers_PowerGroups_DicePoolPowerGroupId",
                        column: x => x.DicePoolPowerGroupId,
                        principalTable: "PowerGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Powers_PowerGroups_PowerGroupId",
                        column: x => x.PowerGroupId,
                        principalTable: "PowerGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BloodBonds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThrallId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegnantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloodBonds", x => x.Id);
                    table.CheckConstraint("CK_BloodBond_NotSelf", "ThrallId <> RegnantId");
                    table.CheckConstraint("CK_BloodBond_Stage", "Stage BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_BloodBonds_Characters_RegnantId",
                        column: x => x.RegnantId,
                        principalTable: "Characters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BloodBonds_Characters_ThrallId",
                        column: x => x.ThrallId,
                        principalTable: "Characters",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Boons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DebtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CashedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CashedInFor = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boons", x => x.Id);
                    table.CheckConstraint("CK_Boon_NotSelf", "DebtorId <> CreditorId");
                    table.ForeignKey(
                        name: "FK_Boons_Characters_CreditorId",
                        column: x => x.CreditorId,
                        principalTable: "Characters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Boons_Characters_DebtorId",
                        column: x => x.DebtorId,
                        principalTable: "Characters",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CharacterAttributes",
                columns: table => new
                {
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Attribute = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Dots = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterAttributes", x => new { x.CharacterId, x.Attribute });
                    table.CheckConstraint("CK_CharacterAttributeRating_Dots", "Dots BETWEEN 0 AND 10");
                    table.ForeignKey(
                        name: "FK_CharacterAttributes_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterCompels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    EndsWhen = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterCompels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterCompels_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterDisciplines",
                columns: table => new
                {
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PowerGroupId = table.Column<int>(type: "int", nullable: false),
                    Dots = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterDisciplines", x => new { x.CharacterId, x.PowerGroupId });
                    table.CheckConstraint("CK_CharacterDiscipline_Dots", "Dots BETWEEN 0 AND 10");
                    table.ForeignKey(
                        name: "FK_CharacterDisciplines_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterDisciplines_PowerGroups_PowerGroupId",
                        column: x => x.PowerGroupId,
                        principalTable: "PowerGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CharacterItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterItems_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterMerits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeritDefinitionId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Dots = table.Column<int>(type: "int", nullable: false),
                    Qualifier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterMerits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterMerits_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterMerits_MeritDefinitions_MeritDefinitionId",
                        column: x => x.MeritDefinitionId,
                        principalTable: "MeritDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CharacterMilestones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Opportunity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Risk = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Choice = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Benefit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterMilestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterMilestones_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterScripts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsMarked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterScripts", x => x.Id);
                    table.CheckConstraint("CK_CharacterScript_Slot", "Slot BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_CharacterScripts_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterSkills",
                columns: table => new
                {
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Skill = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Dots = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterSkills", x => new { x.CharacterId, x.Skill });
                    table.CheckConstraint("CK_CharacterSkillRating_Dots", "Dots BETWEEN 0 AND 10");
                    table.ForeignKey(
                        name: "FK_CharacterSkills_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterSpecialties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Skill = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterSpecialties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterSpecialties_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterStains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterStains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterStains_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterTags",
                columns: table => new
                {
                    CharactersId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterTags", x => new { x.CharactersId, x.TagsId });
                    table.ForeignKey(
                        name: "FK_CharacterTags_Characters_CharactersId",
                        column: x => x.CharactersId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterTags_Tags_TagsId",
                        column: x => x.TagsId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterWounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterWounds", x => x.Id);
                    table.CheckConstraint("CK_CharacterWound_Amount", "Amount >= 0");
                    table.ForeignKey(
                        name: "FK_CharacterWounds_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CorruptionMarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Act = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorruptionMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorruptionMarks_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Relationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromCharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToCharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relationships", x => x.Id);
                    table.CheckConstraint("CK_CharacterRelationship_Target", "ToCharacterId IS NOT NULL OR ToName IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Relationships_Characters_FromCharacterId",
                        column: x => x.FromCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Relationships_Characters_ToCharacterId",
                        column: x => x.ToCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "XpEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PurchaseKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XpEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XpEntries_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterPowers",
                columns: table => new
                {
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PowerId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterPowers", x => new { x.CharacterId, x.PowerId });
                    table.ForeignKey(
                        name: "FK_CharacterPowers_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterPowers_Powers_PowerId",
                        column: x => x.PowerId,
                        principalTable: "Powers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PowerBonusOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PowerId = table.Column<int>(type: "int", nullable: false),
                    SuccessCost = table.Column<int>(type: "int", nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerBonusOptions", x => x.Id);
                    table.CheckConstraint("CK_PowerBonusOption_SuccessCost", "SuccessCost >= 1");
                    table.ForeignKey(
                        name: "FK_PowerBonusOptions_Powers_PowerId",
                        column: x => x.PowerId,
                        principalTable: "Powers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BloodBonds_RegnantId",
                table: "BloodBonds",
                column: "RegnantId");

            migrationBuilder.CreateIndex(
                name: "IX_BloodBonds_ThrallId_RegnantId",
                table: "BloodBonds",
                columns: new[] { "ThrallId", "RegnantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bloodlines_Name",
                table: "Bloodlines",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Boons_CreditorId",
                table: "Boons",
                column: "CreditorId");

            migrationBuilder.CreateIndex(
                name: "IX_Boons_DebtorId_CreditorId",
                table: "Boons",
                columns: new[] { "DebtorId", "CreditorId" });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterCompels_CharacterId",
                table: "CharacterCompels",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterDisciplines_PowerGroupId",
                table: "CharacterDisciplines",
                column: "PowerGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterItems_CharacterId",
                table: "CharacterItems",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterMerits_CharacterId",
                table: "CharacterMerits",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterMerits_MeritDefinitionId",
                table: "CharacterMerits",
                column: "MeritDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterMilestones_CharacterId",
                table: "CharacterMilestones",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterPowers_PowerId",
                table: "CharacterPowers",
                column: "PowerId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_BloodlineId",
                table: "Characters",
                column: "BloodlineId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_CampaignId",
                table: "Characters",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_IsArchived_Tier",
                table: "Characters",
                columns: new[] { "IsArchived", "Tier" });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_Name",
                table: "Characters",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_OwnerUserId",
                table: "Characters",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_PredatorTypeId",
                table: "Characters",
                column: "PredatorTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_SireId",
                table: "Characters",
                column: "SireId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterScripts_CharacterId_Slot",
                table: "CharacterScripts",
                columns: new[] { "CharacterId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CharacterSpecialties_CharacterId",
                table: "CharacterSpecialties",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterStains_CharacterId",
                table: "CharacterStains",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterTags_TagsId",
                table: "CharacterTags",
                column: "TagsId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWounds_CharacterId",
                table: "CharacterWounds",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CorruptionMarks_CharacterId",
                table: "CorruptionMarks",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_MeritDefinitions_Name_Kind",
                table: "MeritDefinitions",
                columns: new[] { "Name", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerBonusOptions_PowerId",
                table: "PowerBonusOptions",
                column: "PowerId");

            migrationBuilder.CreateIndex(
                name: "IX_PowerGroups_BloodlineId",
                table: "PowerGroups",
                column: "BloodlineId");

            migrationBuilder.CreateIndex(
                name: "IX_PowerGroups_Name",
                table: "PowerGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Powers_DicePoolPowerGroupId",
                table: "Powers",
                column: "DicePoolPowerGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Powers_PowerGroupId_Level",
                table: "Powers",
                columns: new[] { "PowerGroupId", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_Powers_PowerGroupId_Name",
                table: "Powers",
                columns: new[] { "PowerGroupId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PredatorTypes_Name",
                table: "PredatorTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_FromCharacterId",
                table: "Relationships",
                column: "FromCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_ToCharacterId",
                table: "Relationships",
                column: "ToCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XpEntries_CharacterId_CreatedAt",
                table: "XpEntries",
                columns: new[] { "CharacterId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BloodBonds");

            migrationBuilder.DropTable(
                name: "Boons");

            migrationBuilder.DropTable(
                name: "CharacterAttributes");

            migrationBuilder.DropTable(
                name: "CharacterCompels");

            migrationBuilder.DropTable(
                name: "CharacterDisciplines");

            migrationBuilder.DropTable(
                name: "CharacterItems");

            migrationBuilder.DropTable(
                name: "CharacterMerits");

            migrationBuilder.DropTable(
                name: "CharacterMilestones");

            migrationBuilder.DropTable(
                name: "CharacterPowers");

            migrationBuilder.DropTable(
                name: "CharacterScripts");

            migrationBuilder.DropTable(
                name: "CharacterSkills");

            migrationBuilder.DropTable(
                name: "CharacterSpecialties");

            migrationBuilder.DropTable(
                name: "CharacterStains");

            migrationBuilder.DropTable(
                name: "CharacterTags");

            migrationBuilder.DropTable(
                name: "CharacterWounds");

            migrationBuilder.DropTable(
                name: "CorruptionMarks");

            migrationBuilder.DropTable(
                name: "PowerBonusOptions");

            migrationBuilder.DropTable(
                name: "Relationships");

            migrationBuilder.DropTable(
                name: "XpEntries");

            migrationBuilder.DropTable(
                name: "MeritDefinitions");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Powers");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "PowerGroups");

            migrationBuilder.DropTable(
                name: "PredatorTypes");

            migrationBuilder.DropTable(
                name: "Bloodlines");
        }
    }
}
