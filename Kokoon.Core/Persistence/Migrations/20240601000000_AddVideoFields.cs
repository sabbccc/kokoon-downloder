using Kokoon.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kokoon.Core.Persistence.Migrations;

/// <summary>
/// Adds video grabber fields (Mode, VideoTitle, ThumbnailUrl, DurationTicks, FormatId)
/// to the DownloadItems table.
/// </summary>
[DbContext(typeof(KokoonDbContext))]
[Migration("20240601000000_AddVideoFields")]
public partial class AddVideoFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Mode",
            table: "DownloadItems",
            type: "TEXT",
            maxLength: 32,
            nullable: false,
            defaultValue: "Http");

        migrationBuilder.AddColumn<string>(
            name: "VideoTitle",
            table: "DownloadItems",
            type: "TEXT",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ThumbnailUrl",
            table: "DownloadItems",
            type: "TEXT",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "DurationTicks",
            table: "DownloadItems",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FormatId",
            table: "DownloadItems",
            type: "TEXT",
            maxLength: 64,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Mode", table: "DownloadItems");
        migrationBuilder.DropColumn(name: "VideoTitle", table: "DownloadItems");
        migrationBuilder.DropColumn(name: "ThumbnailUrl", table: "DownloadItems");
        migrationBuilder.DropColumn(name: "DurationTicks", table: "DownloadItems");
        migrationBuilder.DropColumn(name: "FormatId", table: "DownloadItems");
    }
}
