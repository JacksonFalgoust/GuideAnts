using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using Parity.Core.Model;
using Pgvector;

namespace Parity.Postgres;

/// <summary>
/// The same DocumentChunks entity as SQL Server. Embedding stays float[] and maps to vector(1536)
/// through a value converter: the smallest entity change a real migration would need.
/// </summary>
public sealed class PostgresParityDbContext(DbContextOptions<PostgresParityDbContext> options) : DbContext(options)
{
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();

    public static PostgresParityDbContext Create(NpgsqlDataSource dataSource) =>
        new(new DbContextOptionsBuilder<PostgresParityDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector()) // Microsoft.EntityFrameworkCore.VectorDbContextOptionsBuilderExtensions
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.Entity<DocumentChunkEntity>(b =>
        {
            b.ToTable("DocumentChunks");
            b.HasKey(c => c.Id).HasName("PK_DocumentChunks");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.IndexName).IsRequired().HasMaxLength(255);
            b.Property(c => c.DocumentId).HasMaxLength(255);
            b.Property(c => c.Content).IsRequired();
            b.Property(c => c.Embedding)
                .HasColumnType("vector(1536)")
                .HasConversion(
                    new ValueConverter<float[], Vector>(v => new Vector(new ReadOnlyMemory<float>(v)), v => v.ToArray()),
                    new ValueComparer<float[]>((a, b) => a!.SequenceEqual(b!), v => v.Length, v => v.ToArray()));
            b.HasIndex(c => c.IndexName);
            b.HasIndex(c => c.ProjectId);
            b.HasIndex(c => new { c.ProjectId, c.NotebookId });
        });
    }
}
