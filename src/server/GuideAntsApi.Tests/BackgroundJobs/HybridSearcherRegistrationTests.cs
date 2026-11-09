using FluentAssertions;
using GuideAntsApi.BackgroundJobs;
using GuideAntsApi.BackgroundJobs.Services.Embeddings;
using GuideAntsApi.BackgroundJobs.Services.Search;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using NpgsqlTypes;

namespace GuideAntsApi.Tests.BackgroundJobs;

[TestClass]
public sealed class HybridSearcherRegistrationTests
{
    [TestMethod]
    public void Postgres_provider_resolves_the_postgres_searcher()
    {
        Resolve(new DatabaseProviderInfo(DatabaseProvider.Postgres)).Should().BeOfType<PostgresHybridSearcher>();
    }

    [TestMethod]
    public void SqlServer_provider_resolves_the_sql_server_searcher()
    {
        Resolve(new DatabaseProviderInfo(DatabaseProvider.SqlServer)).Should().BeOfType<HybridSearcher>();
    }

    [TestMethod]
    public void Without_provider_info_the_sql_server_searcher_is_used()
    {
        Resolve(providerInfo: null).Should().BeOfType<HybridSearcher>();
    }

    [TestMethod]
    public void Postgres_query_searches_the_normalized_phrase_with_the_guideants_configuration()
    {
        var sql = PostgresHybridSearcher.Sql;

        sql.Should().Contain("phraseto_tsquery('guideants_english_nostem', guideants_fts_normalize(@query))");
        sql.Should().Contain("ts_rank_cd(dc.\"ContentTsv\", q.tsq, 32)");
        sql.Should().Contain("dc.\"ContentTsv\" @@ q.tsq");
        sql.Should().Contain("LIMIT @candidateLimit");
        sql.Should().Contain("LIMIT @maxResults");
        // Production's top-N quirk: the full-text candidates are taken across the whole table, before the scope filter.
        sql.Should().MatchRegex(@"ft AS \((?:(?!scored AS)[\s\S])*FROM ""DocumentChunks"" dc, q\s+WHERE dc\.""ContentTsv"" @@ q\.tsq\s+ORDER BY rank DESC");
    }

    [TestMethod]
    public void Postgres_command_passes_every_value_as_a_parameter_without_a_size()
    {
        var vector = new float[1536];
        vector[0] = 1f;
        var notebookId = Guid.NewGuid();
        var longQuery = new string('x', 5000);

        using var command = PostgresHybridSearcher.CreateCommand(
            new NpgsqlConnection(), longQuery, vector, "index-a", notebookId, maxResults: 5, vectorWeight: 0.7, textWeight: 0.3);

        command.CommandText.Should().Be(PostgresHybridSearcher.Sql);
        command.Parameters.Select(p => p.ParameterName).Should().BeEquivalentTo(
            "query", "vec", "indexName", "notebookId", "candidateLimit", "maxResults", "vectorWeight", "textWeight");
        // Npgsql truncates a value to Size when Size is set.
        command.Parameters.Should().OnlyContain(p => p.Size == 0);
        command.Parameters["query"].Value.Should().Be(longQuery);
        command.Parameters["query"].NpgsqlDbType.Should().Be(NpgsqlDbType.Text);
        command.Parameters["vec"].Value.Should().BeOfType<Pgvector.Vector>()
            .Which.ToArray().Should().Equal(vector);
        command.Parameters["indexName"].Value.Should().Be("index-a");
        command.Parameters["notebookId"].Value.Should().Be(notebookId);
        command.Parameters["notebookId"].NpgsqlDbType.Should().Be(NpgsqlDbType.Uuid);
        command.Parameters["candidateLimit"].Value.Should().Be(10, "the candidate limit is maxResults times 2, as in HybridSearcher");
        command.Parameters["maxResults"].Value.Should().Be(5);
        command.Parameters["vectorWeight"].Value.Should().Be(0.7);
        command.Parameters["textWeight"].Value.Should().Be(0.3);
    }

    [TestMethod]
    public void Postgres_command_sends_a_missing_notebook_as_a_typed_null()
    {
        using var command = PostgresHybridSearcher.CreateCommand(
            new NpgsqlConnection(), "q", new float[1536], "index-a", notebookId: null, maxResults: 5, vectorWeight: 0.7, textWeight: 0.3);

        command.Parameters["notebookId"].Value.Should().Be(DBNull.Value);
        command.Parameters["notebookId"].NpgsqlDbType.Should().Be(NpgsqlDbType.Uuid);
    }

    [TestMethod]
    public void Postgres_command_replaces_nul_in_the_query_like_stored_text()
    {
        using var command = PostgresHybridSearcher.CreateCommand(
            new NpgsqlConnection(), "a\0b", new float[1536], "index-a", notebookId: null, maxResults: 5, vectorWeight: 0.7, textWeight: 0.3);

        command.Parameters["query"].Value.Should().Be("a\uFFFDb", "Postgres text can't hold U+0000 (parity report #35)");
    }

    private static IHybridSearcher Resolve(DatabaseProviderInfo? providerInfo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (providerInfo is not null)
        {
            services.AddSingleton(providerInfo);
        }

        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseInMemoryDatabase(nameof(HybridSearcherRegistrationTests)));
        services.AddBackgroundJobs(new ConfigurationBuilder().Build());
        services.AddSingleton(Mock.Of<IEmbeddingService>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IHybridSearcher>();
    }
}
