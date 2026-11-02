using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillKro.Data.Migrations
{
    /// <summary>
    /// Renames the ERP link tables (MargExports → ErpExports, MargLinkSettings → ErpLinkSettings) with their keys and
    /// index. Hand-written as renames so the export log and settings are kept; EF scaffolds a drop and re-create.
    /// </summary>
    public partial class RenameMargToErp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_MargExports", schema: "pos", table: "MargExports");
            migrationBuilder.DropPrimaryKey(name: "PK_MargLinkSettings", schema: "pos", table: "MargLinkSettings");

            migrationBuilder.RenameTable(name: "MargExports", schema: "pos", newName: "ErpExports", newSchema: "pos");
            migrationBuilder.RenameTable(name: "MargLinkSettings", schema: "pos", newName: "ErpLinkSettings", newSchema: "pos");

            migrationBuilder.RenameIndex(name: "IX_MargExports_Kind_DocumentId", schema: "pos", table: "ErpExports",
                newName: "IX_ErpExports_Kind_DocumentId");

            migrationBuilder.AddPrimaryKey(name: "PK_ErpExports", schema: "pos", table: "ErpExports", column: "Id");
            migrationBuilder.AddPrimaryKey(name: "PK_ErpLinkSettings", schema: "pos", table: "ErpLinkSettings", column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_ErpExports", schema: "pos", table: "ErpExports");
            migrationBuilder.DropPrimaryKey(name: "PK_ErpLinkSettings", schema: "pos", table: "ErpLinkSettings");

            migrationBuilder.RenameTable(name: "ErpExports", schema: "pos", newName: "MargExports", newSchema: "pos");
            migrationBuilder.RenameTable(name: "ErpLinkSettings", schema: "pos", newName: "MargLinkSettings", newSchema: "pos");

            migrationBuilder.RenameIndex(name: "IX_ErpExports_Kind_DocumentId", schema: "pos", table: "MargExports",
                newName: "IX_MargExports_Kind_DocumentId");

            migrationBuilder.AddPrimaryKey(name: "PK_MargExports", schema: "pos", table: "MargExports", column: "Id");
            migrationBuilder.AddPrimaryKey(name: "PK_MargLinkSettings", schema: "pos", table: "MargLinkSettings", column: "Id");
        }
    }
}
