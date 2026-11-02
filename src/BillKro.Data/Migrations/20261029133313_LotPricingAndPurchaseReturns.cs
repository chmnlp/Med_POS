using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillKro.Data.Migrations
{
    /// <inheritdoc />
    public partial class LotPricingAndPurchaseReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostRate",
                schema: "pos",
                table: "SaleInvoiceLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HospitalRate",
                schema: "pos",
                table: "PurchaseLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LandingCost",
                schema: "pos",
                table: "PurchaseLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginPercent",
                schema: "pos",
                table: "PurchaseLines",
                type: "decimal(7,2)",
                precision: 7,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WholesaleRate",
                schema: "pos",
                table: "PurchaseLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HospitalRate",
                schema: "pos",
                table: "Items",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginPercent",
                schema: "pos",
                table: "Items",
                type: "decimal(7,2)",
                precision: 7,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WholesaleMarginPercent",
                schema: "pos",
                table: "Items",
                type: "decimal(7,2)",
                precision: 7,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HospitalRate",
                schema: "pos",
                table: "Batches",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WholesaleRate",
                schema: "pos",
                table: "Batches",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PurchaseReturns",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FinancialYear = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    PurchaseId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Taxable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RoundOff = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalSchema: "pos",
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "pos",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "pos",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnLines",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    PurchaseLineId = table.Column<int>(type: "int", nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Taxable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "pos",
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "pos",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_PurchaseLines_PurchaseLineId",
                        column: x => x.PurchaseLineId,
                        principalSchema: "pos",
                        principalTable: "PurchaseLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_PurchaseReturns_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "pos",
                        principalTable: "PurchaseReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_BatchId",
                schema: "pos",
                table: "PurchaseReturnLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ItemId",
                schema: "pos",
                table: "PurchaseReturnLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_PurchaseLineId",
                schema: "pos",
                table: "PurchaseReturnLines",
                column: "PurchaseLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ReturnId",
                schema: "pos",
                table: "PurchaseReturnLines",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_CreatedByUserId",
                schema: "pos",
                table: "PurchaseReturns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_PurchaseId",
                schema: "pos",
                table: "PurchaseReturns",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_ReturnDate",
                schema: "pos",
                table: "PurchaseReturns",
                column: "ReturnDate");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_ReturnNo",
                schema: "pos",
                table: "PurchaseReturns",
                column: "ReturnNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_SupplierId",
                schema: "pos",
                table: "PurchaseReturns",
                column: "SupplierId");
            // Back-fill: sales made before cost was recorded take the lot's current cost (best available);
            // purchase lines get their landed cost (taxable value spread over paid + free quantity).
            migrationBuilder.Sql(@"UPDATE l SET CostRate = b.PurchaseRate
                FROM [pos].[SaleInvoiceLines] l JOIN [pos].[Batches] b ON b.Id = l.BatchId;");
            migrationBuilder.Sql(@"UPDATE [pos].[PurchaseLines]
                SET LandingCost = CASE WHEN Qty + FreeQty > 0 THEN ROUND(Taxable / (Qty + FreeQty), 4) ELSE 0 END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseReturnLines",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PurchaseReturns",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "CostRate",
                schema: "pos",
                table: "SaleInvoiceLines");

            migrationBuilder.DropColumn(
                name: "HospitalRate",
                schema: "pos",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "LandingCost",
                schema: "pos",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "MarginPercent",
                schema: "pos",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "WholesaleRate",
                schema: "pos",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "HospitalRate",
                schema: "pos",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "MarginPercent",
                schema: "pos",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "WholesaleMarginPercent",
                schema: "pos",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "HospitalRate",
                schema: "pos",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "WholesaleRate",
                schema: "pos",
                table: "Batches");
        }
    }
}
