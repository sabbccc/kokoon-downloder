using Kokoon.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kokoon.Core.Persistence;

/// <summary>
/// EF Core database context for Kokoon Downloader's SQLite persistence layer.
/// Configures the <see cref="DownloadItemEntity"/> mapping, JSON column storage
/// for segments, indexes, and enum value conversions.
/// </summary>
public class KokoonDbContext : DbContext
{
    /// <summary>All persisted download items.</summary>
    public DbSet<DownloadItemEntity> Downloads => Set<DownloadItemEntity>();

    /// <summary>
    /// Initializes a new instance of <see cref="KokoonDbContext"/>.
    /// </summary>
    /// <param name="options">Options configured by the DI container or factory.</param>
    public KokoonDbContext(DbContextOptions<KokoonDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DownloadItemEntity>(entity =>
        {
            entity.ToTable("DownloadItems");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedNever();

            entity.Property(e => e.Url)
                .IsRequired()
                .HasMaxLength(2048);

            entity.Property(e => e.FileName)
                .IsRequired()
                .HasMaxLength(260);

            entity.Property(e => e.SavePath)
                .IsRequired()
                .HasMaxLength(1024);

            entity.Property(e => e.MimeType)
                .HasMaxLength(256);

            entity.Property(e => e.Referer)
                .HasMaxLength(2048);

            // Download mode stored as string.
            entity.Property(e => e.Mode)
                .HasConversion(new EnumToStringConverter<DownloadMode>())
                .HasMaxLength(32)
                .HasDefaultValue(DownloadMode.Http);

            entity.Property(e => e.VideoTitle)
                .HasMaxLength(500);

            entity.Property(e => e.ThumbnailUrl)
                .HasMaxLength(2048);

            entity.Property(e => e.FormatId)
                .HasMaxLength(64);

            // Store DownloadStatus enum as string for readability and forward compatibility.
            entity.Property(e => e.Status)
                .HasConversion(new EnumToStringConverter<DownloadStatus>())
                .HasMaxLength(32);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // JSON column for segments via EF Core 8 OwnsMany + ToJson().
            entity.OwnsMany(e => e.Segments, segmentBuilder =>
            {
                segmentBuilder.ToJson();

                segmentBuilder.Property(s => s.Status)
                    .HasConversion(new EnumToStringConverter<SegmentStatus>());
            });

            // Indexes for frequent query patterns.
            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_DownloadItems_Status");

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("IX_DownloadItems_CreatedAt");
        });
    }
}
