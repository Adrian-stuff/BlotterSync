using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlotterSync.Migrations
{
    /// <inheritdoc />
    public partial class InitialSupabaseSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Message = table.Column<string>(type: "text", nullable: false),
                    DatePosted = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PostedByOfficerId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SeverityLevel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Categori__19093A0B064FE278", x => x.CategoryId);
                });

            migrationBuilder.CreateTable(
                name: "Citizen",
                columns: table => new
                {
                    CitizenId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContactNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Citizens__6E49FA0C8F1A3E91", x => x.CitizenId);
                });

            migrationBuilder.CreateTable(
                name: "Officers",
                columns: table => new
                {
                    OfficerId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BadgeNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActiveStatus = table.Column<bool>(type: "boolean", nullable: true, defaultValue: true),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Officer")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Officers__2E65577A9E7C9A2C", x => x.OfficerId);
                });

            migrationBuilder.CreateTable(
                name: "Residents",
                columns: table => new
                {
                    ResidentId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContactNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Zone = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Resident__07FB00DC9C87A8DE", x => x.ResidentId);
                });

            migrationBuilder.CreateTable(
                name: "BlotterRecords",
                columns: table => new
                {
                    RecordId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrackingNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    DeskOfficerId = table.Column<int>(type: "integer", nullable: false),
                    IncidentDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReportedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Narrative = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResolutionDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ComplainantId = table.Column<int>(type: "integer", nullable: true),
                    RespondentId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__BlotterR__FBDF78E9C14F26A9", x => x.RecordId);
                    table.ForeignKey(
                        name: "FK__BlotterRe__Categ__3E52440B",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "CategoryId");
                    table.ForeignKey(
                        name: "FK__BlotterRe__Compl__4BAC3F29",
                        column: x => x.ComplainantId,
                        principalTable: "Residents",
                        principalColumn: "ResidentId");
                    table.ForeignKey(
                        name: "FK__BlotterRe__DeskO__3F466844",
                        column: x => x.DeskOfficerId,
                        principalTable: "Officers",
                        principalColumn: "OfficerId");
                    table.ForeignKey(
                        name: "FK__BlotterRe__Respo__4CA06362",
                        column: x => x.RespondentId,
                        principalTable: "Residents",
                        principalColumn: "ResidentId");
                });

            migrationBuilder.CreateTable(
                name: "Involvements",
                columns: table => new
                {
                    InvolvementId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecordId = table.Column<int>(type: "integer", nullable: false),
                    ResidentId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Statement = table.Column<string>(type: "text", nullable: true),
                    CitizenId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Involvem__A7B85E33D5021004", x => x.InvolvementId);
                    table.ForeignKey(
                        name: "FK_Involvements_Citizen_CitizenId",
                        column: x => x.CitizenId,
                        principalTable: "Citizen",
                        principalColumn: "CitizenId");
                    table.ForeignKey(
                        name: "FK__Involveme__Recor__44FF419A",
                        column: x => x.RecordId,
                        principalTable: "BlotterRecords",
                        principalColumn: "RecordId");
                    table.ForeignKey(
                        name: "FK__Involveme__Resid__45F365D3",
                        column: x => x.ResidentId,
                        principalTable: "Residents",
                        principalColumn: "ResidentId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlotterRecords_CategoryId",
                table: "BlotterRecords",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BlotterRecords_ComplainantId",
                table: "BlotterRecords",
                column: "ComplainantId");

            migrationBuilder.CreateIndex(
                name: "IX_BlotterRecords_DeskOfficerId",
                table: "BlotterRecords",
                column: "DeskOfficerId");

            migrationBuilder.CreateIndex(
                name: "IX_BlotterRecords_RespondentId",
                table: "BlotterRecords",
                column: "RespondentId");

            migrationBuilder.CreateIndex(
                name: "UQ__BlotterR__784DB3D9D658DE32",
                table: "BlotterRecords",
                column: "TrackingNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Involvements_CitizenId",
                table: "Involvements",
                column: "CitizenId");

            migrationBuilder.CreateIndex(
                name: "IX_Involvements_RecordId",
                table: "Involvements",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_Involvements_ResidentId",
                table: "Involvements",
                column: "ResidentId");

            migrationBuilder.CreateIndex(
                name: "UQ__Officers__D110FD56D35C9A0C",
                table: "Officers",
                column: "BadgeNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Officers_Username",
                table: "Officers",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.DropTable(
                name: "Involvements");

            migrationBuilder.DropTable(
                name: "Citizen");

            migrationBuilder.DropTable(
                name: "BlotterRecords");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Residents");

            migrationBuilder.DropTable(
                name: "Officers");
        }
    }
}
