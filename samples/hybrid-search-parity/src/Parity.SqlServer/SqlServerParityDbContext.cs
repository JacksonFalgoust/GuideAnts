using Microsoft.EntityFrameworkCore;
using Parity.Core.Model;

namespace Parity.SqlServer;

/// <summary>Production's DocumentChunks table: same columns, types and primary key name.</summary>
public sealed class SqlServerParityDbContext(DbContextOptions<SqlServerParityDbContext> options) : DbContext(options)
{
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();

    public static SqlServerParityDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<SqlServerParityDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.UseVectorSearch();
                sql.CommandTimeout(120);
            })
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentChunkEntity>(b =>
        {
            b.ToTable("DocumentChunks");
            b.HasKey(c => c.Id).HasName("PK_DocumentChunks");
            b.Property(c => c.Id).ValueGeneratedNever(); // deterministic IDs, identical on both engines
            b.Property(c => c.IndexName).IsRequired().HasMaxLength(255);
            b.Property(c => c.DocumentId).HasMaxLength(255);
            b.Property(c => c.Content).IsRequired();
            b.Property(c => c.Embedding).HasColumnType("vector(1536)");
            b.HasIndex(c => c.IndexName);
            b.HasIndex(c => c.ProjectId);
            b.HasIndex(c => new { c.ProjectId, c.NotebookId });
        });
    }
}
