using Kokoon.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kokoon.Core.Persistence.Migrations;

/// <summary>
/// Initial EF Core migration that creates the <c>DownloadItems</c> table
/// with all columns, indexes, and the JSON column for segment data.
/// </summary>
[DbContext(typeof(KokoonDbContext))]
[Migration("20240101000000_InitialCreate")]
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DownloadItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                SavePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                DownloadedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                SegmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                SupportsRanges = table.Column<bool>(type: "INTEGER", nullable: false),
                MimeType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                Referer = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                Segments = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DownloadItems", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DownloadItems_Status",
            table: "DownloadItems",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_DownloadItems_CreatedAt",
            table: "DownloadItems",
            column: "CreatedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DownloadItems");
    }
}
