using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.EsrpSign.Sqlite.Migrations
{
    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public partial class AddCompanySigningAndPersonalApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanySigningKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PrivateEnvelope = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySigningKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PersonalApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonalApiKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanySigningKeys_Fingerprint",
                table: "CompanySigningKeys",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalApiKeys_KeyHash",
                table: "PersonalApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalApiKeys_UserId",
                table: "PersonalApiKeys",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanySigningKeys");

            migrationBuilder.DropTable(
                name: "PersonalApiKeys");
        }
    }
}
