using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MargPos.Data.Migrations
{
    /// <inheritdoc />
    public partial class BatchBarcodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                schema: "pos",
                table: "Batches",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_Barcode",
                schema: "pos",
                table: "Batches",
                column: "Barcode",
                unique: true,
                filter: "[Barcode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Batches_Barcode",
                schema: "pos",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "Barcode",
                schema: "pos",
                table: "Batches");
        }
    }
}
