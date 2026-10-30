using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MargPos.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                schema: "pos",
                table: "StockMovements",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchaseRate",
                schema: "pos",
                table: "Items",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchaseRate",
                schema: "pos",
                table: "Batches",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.CreateTable(
                name: "ItemAliases",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    SupplierItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemAliases_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "pos",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemAliases_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "pos",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Purchases",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FinancialYear = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    SupplierInvoiceNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierInvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Taxable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RoundOff = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Purchases_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "pos",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Purchases_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "pos",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustments",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdjustmentNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    QtyChange = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "pos",
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "pos",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "pos",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "pos",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseLines",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    FreeQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Mrp = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaleRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Taxable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "pos",
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "pos",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseLines_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalSchema: "pos",
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_UserId",
                schema: "pos",
                table: "StockMovements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemAliases_ItemId",
                schema: "pos",
                table: "ItemAliases",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemAliases_SupplierId_SupplierItemCode",
                schema: "pos",
                table: "ItemAliases",
                columns: new[] { "SupplierId", "SupplierItemCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_BatchId",
                schema: "pos",
                table: "PurchaseLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_ItemId",
                schema: "pos",
                table: "PurchaseLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_PurchaseId",
                schema: "pos",
                table: "PurchaseLines",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_CreatedByUserId",
                schema: "pos",
                table: "Purchases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_PurchaseNo",
                schema: "pos",
                table: "Purchases",
                column: "PurchaseNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_ReceivedAt",
                schema: "pos",
                table: "Purchases",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_SupplierId_SupplierInvoiceNo",
                schema: "pos",
                table: "Purchases",
                columns: new[] { "SupplierId", "SupplierInvoiceNo" });

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_AdjustmentNo",
                schema: "pos",
                table: "StockAdjustments",
                column: "AdjustmentNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_BatchId",
                schema: "pos",
                table: "StockAdjustments",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_Date",
                schema: "pos",
                table: "StockAdjustments",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_ItemId",
                schema: "pos",
                table: "StockAdjustments",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_SupplierId",
                schema: "pos",
                table: "StockAdjustments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_UserId",
                schema: "pos",
                table: "StockAdjustments",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Users_UserId",
                schema: "pos",
                table: "StockMovements",
                column: "UserId",
                principalSchema: "pos",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Users_UserId",
                schema: "pos",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "ItemAliases",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PurchaseLines",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "StockAdjustments",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "Purchases",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_UserId",
                schema: "pos",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "Note",
                schema: "pos",
                table: "StockMovements");

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchaseRate",
                schema: "pos",
                table: "Items",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchaseRate",
                schema: "pos",
                table: "Batches",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);
        }
    }
}
