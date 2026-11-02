using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillKro.Data.Migrations
{
    /// <inheritdoc />
    public partial class MargLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MargExports",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MargExports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MargLinkSettings",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    MethodFolder = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    CompanyCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SendFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PollMinutes = table.Column<int>(type: "int", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MargLinkSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MargExports_Kind_DocumentId",
                schema: "pos",
                table: "MargExports",
                columns: new[] { "Kind", "DocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MargExports",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "MargLinkSettings",
                schema: "pos");
        }
    }
}
