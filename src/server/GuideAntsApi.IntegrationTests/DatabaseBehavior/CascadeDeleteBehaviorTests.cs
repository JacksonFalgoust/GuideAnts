using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// A DELETE whose cascades also remove the rows that reference the deleted row through a <c>NO ACTION</c> foreign
/// key. SQL Server checks a <c>NO ACTION</c> foreign key after every cascade of the statement, so such a delete
/// succeeds. Postgres runs each check as a trigger; where the check could fire before the cascade, the Postgres
/// baseline declares the foreign key <c>DEFERRABLE INITIALLY DEFERRED</c> so the check runs at commit, after every
/// cascade. The tests run on both providers against the suite's database (no web host) and expect the SQL Server
/// outcome. The negative cases prove the <c>NO ACTION</c> check itself is kept.
/// </summary>
/// <remarks>
/// Foreign keys covered (parity report rows #19, #23 to #25):
/// <list type="bullet">
/// <item>deferred on Postgres: <c>UserRoles.AssignedByUserId</c>, <c>GuideMembers.AssistantId</c>,
/// <c>MessageAttachments.NotebookFileId</c></item>
/// <item>not deferred, because Postgres removes the referencing rows before the check by itself (the cascade into the
/// referencing table is no deeper than the one into the referenced table): <c>NotebookConversationMessages</c> to
/// <c>ConversationTurns</c>, <c>AgentInvocations.ParentInvocationId</c>, <c>ContentFileVersions.OriginVersionId</c></item>
/// </list>
/// </remarks>
[TestClass]
public sealed class CascadeDeleteBehaviorTests
{
    private const string AssignedByForeignKey = "FK_UserRoles_Users_AssignedByUserId";
    private const string CrewMemberForeignKey = "FK_GuideMembers_Assistants_AssistantId";
    private const string AttachmentFileForeignKey = "FK_MessageAttachments_NotebookFiles_NotebookFileId";

    private string _connectionString = null!;
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private readonly List<Guid> _userIds = [];
    private readonly List<Guid> _assistantIds = [];
    private readonly List<Guid> _projectIds = [];

    [TestInitialize]
    public async Task TestInitialize()
    {
        _connectionString = await TestContainerManager.Instance.GetConnectionStringAsync();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder,
            TestContainerManager.Instance.Provider,
            _connectionString,
            new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true));
        _options = builder.Options;
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        // Children first, so the cleanup itself never depends on the behavior under test.
        await using (var db = CreateContext())
        {
            await db.UserRoles
                .Where(r => _userIds.Contains(r.UserId) || (r.AssignedByUserId != null && _userIds.Contains(r.AssignedByUserId.Value)))
                .ExecuteDeleteAsync();
            await db.Users.Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();

            await db.MessageAttachments
                .Where(a => _projectIds.Contains(a.Message.NotebookConversation.Notebook.ProjectId))
                .ExecuteDeleteAsync();
            await db.AgentInvocations
                .Where(i => _projectIds.Contains(i.ParentConversation.Notebook.ProjectId))
                .ExecuteDeleteAsync();
            await db.NotebookConversationMessages
                .Where(m => _projectIds.Contains(m.NotebookConversation.Notebook.ProjectId))
                .ExecuteDeleteAsync();
            await db.Notebooks.Where(n => _projectIds.Contains(n.ProjectId)).ExecuteDeleteAsync();
            await db.ContentFileVersions
                .Where(v => _projectIds.Contains(v.ContentFile.ProjectId))
                .ExecuteDeleteAsync();
            await db.ContentFiles.Where(f => _projectIds.Contains(f.ProjectId)).ExecuteDeleteAsync();
            await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();

            await db.GuideMembers
                .Where(m => _assistantIds.Contains(m.GuideId) || _assistantIds.Contains(m.AssistantId))
                .ExecuteDeleteAsync();
            await db.Assistants.Where(a => _assistantIds.Contains(a.Id)).ExecuteDeleteAsync();
        }
    }

    // ------------------------------------------------------------------
    // UserRoles: UserId is CASCADE, AssignedByUserId is NO ACTION (#19)
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Deleting_a_self_assigned_user_with_ExecuteDelete_removes_the_user_and_its_role()
    {
        var userId = await SeedUserAsync(assignedBy: Self);

        await using (var db = CreateContext())
        {
            (await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync()).Should().Be(1);
        }

        await AssertUsersGoneWithTheirRolesAsync(userId);
    }

    [TestMethod]
    public async Task Deleting_a_self_assigned_user_with_Remove_and_SaveChanges_removes_the_user_and_its_role()
    {
        var userId = await SeedUserAsync(assignedBy: Self);

        await using (var db = CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }

        await AssertUsersGoneWithTheirRolesAsync(userId);
    }

    [TestMethod]
    public async Task Deleting_users_who_assigned_each_others_roles_in_one_statement_removes_both()
    {
        var first = await SeedUserAsync(assignedBy: null);
        var second = await SeedUserAsync(assignedBy: first);
        await using (var db = CreateContext())
        {
            await db.UserRoles.Where(r => r.UserId == first).ExecuteUpdateAsync(s => s.SetProperty(r => r.AssignedByUserId, second));

            (await db.Users.Where(u => u.Id == first || u.Id == second).ExecuteDeleteAsync()).Should().Be(2);
        }

        await AssertUsersGoneWithTheirRolesAsync(first, second);
    }

    [TestMethod]
    public async Task Deleting_a_user_who_assigned_another_users_role_fails_with_ExecuteDelete()
    {
        var assigner = await SeedUserAsync(assignedBy: Self);
        var assignee = await SeedUserAsync(assignedBy: assigner);

        await using (var db = CreateContext())
        {
            var delete = () => db.Users.Where(u => u.Id == assigner).ExecuteDeleteAsync();

            (await delete.Should().ThrowAsync<Exception>()).Which.Should().Match<Exception>(e => IsForeignKeyViolation(e, AssignedByForeignKey));
        }

        await AssertUsersStillHaveTheirRolesAsync(assigner, assignee);
    }

    [TestMethod]
    public async Task Deleting_a_user_who_assigned_another_users_role_fails_with_SaveChanges()
    {
        var assigner = await SeedUserAsync(assignedBy: Self);
        var assignee = await SeedUserAsync(assignedBy: assigner);

        await using (var db = CreateContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == assigner));
            var save = () => db.SaveChangesAsync();

            (await save.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<Exception>(e => IsForeignKeyViolation(e, AssignedByForeignKey));
        }

        await AssertUsersStillHaveTheirRolesAsync(assigner, assignee);
    }

    /// <summary>
    /// The accepted residual difference of a deferred check (parity report #25): inside an explicit transaction,
    /// SQL Server rejects the violating statement itself, while Postgres runs the statement and rejects the commit.
    /// Either way nothing is deleted.
    /// </summary>
    [TestMethod]
    public async Task In_a_transaction_sql_server_rejects_the_violating_delete_and_postgres_rejects_the_commit()
    {
        var assigner = await SeedUserAsync(assignedBy: Self);
        var assignee = await SeedUserAsync(assignedBy: assigner);

        await using (var db = CreateContext())
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var delete = () => db.Users.Where(u => u.Id == assigner).ExecuteDeleteAsync();

                if (TestContainerManager.Instance.Provider == DatabaseProvider.SqlServer)
                {
                    (await delete.Should().ThrowAsync<SqlException>()).Which.Should().Match<Exception>(e => IsForeignKeyViolation(e, AssignedByForeignKey));
                    await transaction.RollbackAsync();
                }
                else
                {
                    (await delete()).Should().Be(1);
                    var commit = () => transaction.CommitAsync();
                    (await commit.Should().ThrowAsync<PostgresException>()).Which.Should().Match<Exception>(e => IsForeignKeyViolation(e, AssignedByForeignKey));
                }
            });
        }

        await AssertUsersStillHaveTheirRolesAsync(assigner, assignee);
    }

    /// <summary>
    /// The other side of #25: a violation that a later statement of the same transaction repairs is accepted on
    /// Postgres (the deferred check sees the final state) and rejected on SQL Server at the violating statement.
    /// </summary>
    [TestMethod]
    public async Task In_a_transaction_postgres_accepts_a_violation_repaired_before_commit()
    {
        var assigner = await SeedUserAsync(assignedBy: Self);
        var assignee = await SeedUserAsync(assignedBy: assigner);

        await using (var db = CreateContext())
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var delete = () => db.Users.Where(u => u.Id == assigner).ExecuteDeleteAsync();

                if (TestContainerManager.Instance.Provider == DatabaseProvider.SqlServer)
                {
                    (await delete.Should().ThrowAsync<SqlException>()).Which.Should().Match<Exception>(e => IsForeignKeyViolation(e, AssignedByForeignKey));
                    await transaction.RollbackAsync();
                }
                else
                {
                    (await delete()).Should().Be(1);
                    (await db.UserRoles.Where(r => r.UserId == assignee).ExecuteDeleteAsync()).Should().Be(1);
                    await transaction.CommitAsync();
                }
            });
        }

        await using (var db = CreateContext())
        {
            var assignerExists = await db.Users.AnyAsync(u => u.Id == assigner);
            assignerExists.Should().Be(TestContainerManager.Instance.Provider == DatabaseProvider.SqlServer);
        }
    }

    // ------------------------------------------------------------------
    // GuideMembers: GuideId is CASCADE, AssistantId is NO ACTION (#23)
    // ------------------------------------------------------------------

    /// <summary>
    /// The guide's cascade removes the membership row that the member's NO ACTION check looks for. Both insertion
    /// orders run, so the rows are deleted (and their triggers queued) in both orders.
    /// </summary>
    [TestMethod]
    [DataRow(true, DisplayName = "member inserted first")]
    [DataRow(false, DisplayName = "guide inserted first")]
    public async Task Deleting_a_guide_and_its_crew_member_in_one_statement_removes_both(bool memberFirst)
    {
        var (guideId, memberId) = await SeedGuideWithMemberAsync(memberFirst);

        await using (var db = CreateContext())
        {
            (await db.Assistants.Where(a => a.Id == guideId || a.Id == memberId).ExecuteDeleteAsync()).Should().Be(2);
        }

        await using (var db = CreateContext())
        {
            (await db.Assistants.CountAsync(a => a.Id == guideId || a.Id == memberId)).Should().Be(0);
            (await db.GuideMembers.CountAsync(m => m.GuideId == guideId)).Should().Be(0);
        }
    }

    [TestMethod]
    public async Task Deleting_a_crew_member_whose_guide_stays_fails()
    {
        var (guideId, memberId) = await SeedGuideWithMemberAsync(memberFirst: true);

        await using (var db = CreateContext())
        {
            var delete = () => db.Assistants.Where(a => a.Id == memberId).ExecuteDeleteAsync();

            (await delete.Should().ThrowAsync<Exception>()).Which.Should().Match<Exception>(e => IsForeignKeyViolation(e, CrewMemberForeignKey));
        }

        await using (var db = CreateContext())
        {
            (await db.GuideMembers.CountAsync(m => m.GuideId == guideId && m.AssistantId == memberId)).Should().Be(1);
        }
    }

    // ------------------------------------------------------------------
    // MessageAttachments.NotebookFileId is NO ACTION; the notebook cascades to both its files and, through its
    // conversations and messages, to the attachments (#24)
    // ------------------------------------------------------------------

    /// <summary>Same shape as <c>NotebookService.DeleteAsync</c>: only the notebook is loaded and removed.</summary>
    [TestMethod]
    public async Task Deleting_a_notebook_removes_message_attachments_that_reference_its_files()
    {
        var notebook = await SeedNotebookWithAttachmentAsync();

        await using (var db = CreateContext())
        {
            db.Notebooks.Remove(await db.Notebooks.SingleAsync(n => n.Id == notebook.NotebookId));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            (await db.Notebooks.AnyAsync(n => n.Id == notebook.NotebookId)).Should().BeFalse();
            (await db.NotebookFiles.AnyAsync(f => f.Id == notebook.FileId)).Should().BeFalse();
            (await db.MessageAttachments.AnyAsync(a => a.NotebookFileId == notebook.FileId)).Should().BeFalse();
            (await db.NotebookConversations.AnyAsync(c => c.Id == notebook.ConversationId)).Should().BeFalse();
        }
    }

    [TestMethod]
    public async Task Deleting_a_notebook_file_that_a_message_still_attaches_fails()
    {
        var notebook = await SeedNotebookWithAttachmentAsync();

        await using (var db = CreateContext())
        {
            var delete = () => db.NotebookFiles.Where(f => f.Id == notebook.FileId).ExecuteDeleteAsync();

            (await delete.Should().ThrowAsync<Exception>()).Which.Should().Match<Exception>(e => IsForeignKeyViolation(e, AttachmentFileForeignKey));
        }

        await using (var db = CreateContext())
        {
            (await db.NotebookFiles.AnyAsync(f => f.Id == notebook.FileId)).Should().BeTrue();
        }
    }

    // ------------------------------------------------------------------
    // Shapes Postgres already orders correctly (not deferred)
    // ------------------------------------------------------------------

    /// <summary>
    /// Same shape as <c>ConversationCommandService.DeleteConversationAsync</c>. The conversation cascades to its
    /// turns and to its messages, and each message references its turn through a NO ACTION foreign key.
    /// </summary>
    [TestMethod]
    public async Task Deleting_a_conversation_removes_its_turns_and_the_messages_that_reference_them()
    {
        var notebook = await SeedNotebookWithAttachmentAsync();

        await using (var db = CreateContext())
        {
            db.NotebookConversations.Remove(await db.NotebookConversations.SingleAsync(c => c.Id == notebook.ConversationId));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            (await db.ConversationTurns.AnyAsync(t => t.NotebookConversationId == notebook.ConversationId)).Should().BeFalse();
            (await db.NotebookConversationMessages.AnyAsync(m => m.NotebookConversationId == notebook.ConversationId)).Should().BeFalse();
            (await db.MessageAttachments.AnyAsync(a => a.NotebookFileId == notebook.FileId)).Should().BeFalse();
        }
    }

    [TestMethod]
    public async Task Deleting_a_conversation_removes_agent_invocations_that_reference_each_other()
    {
        var notebook = await SeedNotebookWithAttachmentAsync();
        var assistantId = await SeedAssistantAsync(AssistantKind.Assistant);
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        await using (var db = CreateContext())
        {
            db.AgentInvocations.Add(NewInvocation(parentId, notebook.ConversationId, assistantId, parentInvocationId: null));
            await db.SaveChangesAsync();
            db.AgentInvocations.Add(NewInvocation(childId, notebook.ConversationId, assistantId, parentInvocationId: parentId));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            db.NotebookConversations.Remove(await db.NotebookConversations.SingleAsync(c => c.Id == notebook.ConversationId));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            (await db.AgentInvocations.AnyAsync(i => i.Id == parentId || i.Id == childId)).Should().BeFalse();
        }
    }

    [TestMethod]
    public async Task Deleting_content_files_whose_versions_reference_each_other_in_one_statement_removes_them()
    {
        var projectId = await SeedProjectAsync();
        var firstFile = NewContentFile(projectId, "first.md");
        var secondFile = NewContentFile(projectId, "second.md");
        var firstVersion = NewContentFileVersion(firstFile, originVersionId: null);
        var secondVersion = NewContentFileVersion(secondFile, originVersionId: firstVersion.Id);

        await using (var db = CreateContext())
        {
            db.ContentFiles.AddRange(firstFile, secondFile);
            await db.SaveChangesAsync();
            db.ContentFileVersions.Add(firstVersion);
            await db.SaveChangesAsync();
            db.ContentFileVersions.Add(secondVersion);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            (await db.ContentFiles.Where(f => f.Id == firstFile.Id || f.Id == secondFile.Id).ExecuteDeleteAsync()).Should().Be(2);
        }

        await using (var db = CreateContext())
        {
            (await db.ContentFileVersions.AnyAsync(v => v.Id == firstVersion.Id || v.Id == secondVersion.Id)).Should().BeFalse();
        }
    }

    // ------------------------------------------------------------------
    // Why the fix does not depend on constraint creation order (Postgres only)
    // ------------------------------------------------------------------

    /// <summary>
    /// Postgres fires the triggers of one row event in trigger-name order, and a foreign key's trigger names embed
    /// OIDs, so without deferral the outcome follows the creation order of the two constraints (and, once OIDs gain a
    /// digit, not even that). With the NO ACTION foreign key <c>DEFERRABLE INITIALLY DEFERRED</c> the check runs at
    /// commit, after the cascade, in both creation orders. Runs in a scratch schema of the suite's database.
    /// </summary>
    [TestMethod]
    [DataRow(true, DisplayName = "NO ACTION foreign key created first")]
    [DataRow(false, DisplayName = "CASCADE foreign key created first")]
    public async Task Postgres_deferred_no_action_check_waits_for_the_cascade_whatever_the_creation_order(bool noActionFirst)
    {
        DatabaseTestProvider.RequirePostgres();

        var schema = $"cascade_order_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        try
        {
            var noAction = $"""ALTER TABLE {schema}.roles ADD CONSTRAINT roles_assigner_fk FOREIGN KEY (assigner) REFERENCES {schema}.users (id) DEFERRABLE INITIALLY DEFERRED;""";
            var cascade = $"""ALTER TABLE {schema}.roles ADD CONSTRAINT roles_owner_fk FOREIGN KEY (owner) REFERENCES {schema}.users (id) ON DELETE CASCADE;""";
            await ExecuteAsync(connection, $"""
                CREATE SCHEMA {schema};
                CREATE TABLE {schema}.users (id int PRIMARY KEY);
                CREATE TABLE {schema}.roles (id int PRIMARY KEY, owner int NOT NULL, assigner int);
                {(noActionFirst ? noAction : cascade)}
                {(noActionFirst ? cascade : noAction)}
                """);

            // Deferred: the self-referencing delete succeeds in either creation order.
            (await DeleteSelfAssignedUserAsync(connection, schema)).Should().BeNull();

            // Not deferred (same triggers, same names): the outcome follows the delete triggers' name order.
            await ExecuteAsync(connection, $"ALTER TABLE {schema}.roles ALTER CONSTRAINT roles_assigner_fk NOT DEFERRABLE;");
            var triggerNames = await ReadDeleteTriggerNamesAsync(connection, schema);
            var cascadeFiresFirst = string.CompareOrdinal(triggerNames["roles_owner_fk"], triggerNames["roles_assigner_fk"]) < 0;

            var immediateError = await DeleteSelfAssignedUserAsync(connection, schema);
            if (cascadeFiresFirst)
            {
                immediateError.Should().BeNull();
            }
            else
            {
                immediateError.Should().NotBeNull();
                immediateError!.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
                immediateError.ConstraintName.Should().Be("roles_assigner_fk");
            }
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {schema} CASCADE;");
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static readonly Guid Self = Guid.Empty;

    private ApplicationDbContext CreateContext() => new(_options);

    /// <summary>Seeds a user with one role; <paramref name="assignedBy"/> <see cref="Self"/> makes the user its own assigner.</summary>
    private async Task<Guid> SeedUserAsync(Guid? assignedBy)
    {
        var userId = Guid.NewGuid();
        _userIds.Add(userId);

        await using var db = CreateContext();
        db.Users.Add(new User { Id = userId, Name = "Cascade test user", Email = $"cascade-{userId:N}@cascade.local" });
        db.UserRoles.Add(new UserRole
        {
            UserId = userId,
            Role = Role.Admin,
            AssignedByUserId = assignedBy == Self ? userId : assignedBy
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task AssertUsersGoneWithTheirRolesAsync(params Guid[] userIds)
    {
        await using var db = CreateContext();
        (await db.Users.CountAsync(u => userIds.Contains(u.Id))).Should().Be(0);
        (await db.UserRoles.CountAsync(r => userIds.Contains(r.UserId))).Should().Be(0, "the cascade removes the roles");
    }

    private async Task AssertUsersStillHaveTheirRolesAsync(params Guid[] userIds)
    {
        await using var db = CreateContext();
        (await db.Users.CountAsync(u => userIds.Contains(u.Id))).Should().Be(userIds.Length);
        (await db.UserRoles.CountAsync(r => userIds.Contains(r.UserId))).Should().Be(userIds.Length);
    }

    private async Task<Guid> SeedAssistantAsync(AssistantKind kind)
    {
        var assistantId = Guid.NewGuid();
        _assistantIds.Add(assistantId);

        await using var db = CreateContext();
        db.Assistants.Add(new Assistant { Id = assistantId, Name = $"Cascade {kind} {assistantId:N}", Kind = kind });
        await db.SaveChangesAsync();
        return assistantId;
    }

    private async Task<(Guid GuideId, Guid MemberId)> SeedGuideWithMemberAsync(bool memberFirst)
    {
        Guid guideId, memberId;
        if (memberFirst)
        {
            memberId = await SeedAssistantAsync(AssistantKind.Assistant);
            guideId = await SeedAssistantAsync(AssistantKind.Guide);
        }
        else
        {
            guideId = await SeedAssistantAsync(AssistantKind.Guide);
            memberId = await SeedAssistantAsync(AssistantKind.Assistant);
        }

        await using var db = CreateContext();
        db.GuideMembers.Add(new GuideMember { GuideId = guideId, AssistantId = memberId });
        await db.SaveChangesAsync();
        return (guideId, memberId);
    }

    private async Task<Guid> SeedProjectAsync()
    {
        var projectId = Guid.NewGuid();
        _projectIds.Add(projectId);

        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = projectId, Title = "Cascade test project", Slug = $"cascade-{projectId:N}" });
        await db.SaveChangesAsync();
        return projectId;
    }

    /// <summary>
    /// A notebook with one file and one conversation whose turn 0 has a user message that attaches the file.
    /// </summary>
    private async Task<(Guid NotebookId, Guid FileId, Guid ConversationId)> SeedNotebookWithAttachmentAsync()
    {
        var projectId = await SeedProjectAsync();
        var notebook = new Notebook { Title = "Cascade notebook", Slug = $"cascade-{Guid.NewGuid():N}", ProjectId = projectId };
        var file = new NotebookFile
        {
            NotebookId = notebook.Id,
            RelativePath = "attached.md",
            FileSize = 1,
            LastModifiedUtc = DateTime.UtcNow,
            FileHash = "hash"
        };
        file.GenerateDocumentId(notebook.Id);
        var conversation = new NotebookConversation { NotebookId = notebook.Id, Title = "Cascade conversation" };
        var turn = new ConversationTurn
        {
            NotebookConversationId = conversation.Id,
            TurnIndex = 0,
            AssistantName = "Cascade assistant",
            Instructions = "Answer."
        };
        var message = new NotebookConversationMessage
        {
            NotebookConversationId = conversation.Id,
            Role = ChatRole.User,
            Content = "See the file.",
            TurnIndex = 0,
            MessageSequence = 0
        };
        var attachment = new MessageAttachment { MessageId = message.Id, NotebookFileId = file.Id, OrderIndex = 0 };

        await using var db = CreateContext();
        db.Notebooks.Add(notebook);
        db.NotebookFiles.Add(file);
        db.NotebookConversations.Add(conversation);
        db.ConversationTurns.Add(turn);
        db.NotebookConversationMessages.Add(message);
        db.MessageAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return (notebook.Id, file.Id, conversation.Id);
    }

    private static AgentInvocation NewInvocation(Guid id, Guid conversationId, Guid assistantId, Guid? parentInvocationId) => new()
    {
        Id = id,
        ParentConversationId = conversationId,
        ParentTurnIndex = 0,
        ParentInvocationId = parentInvocationId,
        AssistantId = assistantId,
        AssistantName = "Cascade assistant",
        Instructions = "Help.",
        Depth = parentInvocationId is null ? 0 : 1
    };

    private static ContentFile NewContentFile(Guid projectId, string fileName)
    {
        var file = new ContentFile
        {
            FileName = fileName,
            Path = fileName,
            RelativePath = fileName,
            FileSize = 1,
            ContentType = "text/markdown",
            ProjectId = projectId
        };
        file.GenerateDocumentId();
        return file;
    }

    private static ContentFileVersion NewContentFileVersion(ContentFile file, Guid? originVersionId) => new()
    {
        Id = Guid.NewGuid(),
        ContentFileId = file.Id,
        VersionNumber = 1,
        FileName = file.FileName,
        Path = file.Path,
        RelativePath = file.RelativePath,
        FileSize = file.FileSize,
        ContentType = file.ContentType,
        OriginVersionId = originVersionId
    };

    /// <summary>
    /// True when the exception chain holds a foreign key violation of <paramref name="constraint"/>:
    /// SQL Server error 547 naming it, or Postgres SQLSTATE 23503 on it.
    /// </summary>
    private static bool IsForeignKeyViolation(Exception exception, string constraint)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case SqlException sql when sql.Number == 547:
                    return sql.Message.Contains($"\"{constraint}\"", StringComparison.Ordinal);
                case PostgresException postgres when postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation:
                    return postgres.ConstraintName == constraint;
            }
        }

        return false;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Inserts user 1 with a role it assigned itself, deletes the user, and returns the error (null on success).</summary>
    private static async Task<PostgresException?> DeleteSelfAssignedUserAsync(NpgsqlConnection connection, string schema)
    {
        await ExecuteAsync(connection, $"""
            INSERT INTO {schema}.users (id) VALUES (1);
            INSERT INTO {schema}.roles (id, owner, assigner) VALUES (1, 1, 1);
            """);
        try
        {
            await ExecuteAsync(connection, $"DELETE FROM {schema}.users WHERE id = 1;");
        }
        catch (PostgresException error)
        {
            await ExecuteAsync(connection, $"DELETE FROM {schema}.roles; DELETE FROM {schema}.users;");
            return error;
        }

        await using var count = new NpgsqlCommand($"SELECT (SELECT count(*) FROM {schema}.users) + (SELECT count(*) FROM {schema}.roles);", connection);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(0, "the user and its cascaded role are gone");
        return null;
    }

    /// <summary>The name of each foreign key's ON DELETE trigger on <c>users</c>, by constraint name.</summary>
    private static async Task<Dictionary<string, string>> ReadDeleteTriggerNamesAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = new NpgsqlCommand("""
            SELECT con.conname, t.tgname
            FROM pg_trigger t
            JOIN pg_constraint con ON con.oid = t.tgconstraint
            JOIN pg_proc p ON p.oid = t.tgfoid
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = 'users' AND p.proname LIKE 'RI_FKey_%_del'
            """, connection);
        command.Parameters.AddWithValue("schema", schema);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            names[reader.GetString(0)] = reader.GetString(1);
        }

        names.Keys.Should().BeEquivalentTo("roles_assigner_fk", "roles_owner_fk");
        return names;
    }
}
