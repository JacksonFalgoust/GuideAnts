using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GuideAntsApi.DataModel.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PostgresBaselineV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:guideants_ci", "und-u-ks-level2,und-u-ks-level2,icu,False")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "ApplicationSettings",
                columns: table => new
                {
                    SectionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    JsonValue = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationSettings", x => x.SectionName);
                });

            migrationBuilder.CreateTable(
                name: "AssistantAuthProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    AuthType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "guideants_ci"),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "guideants_ci"),
                    Tenant = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "guideants_ci"),
                    HeaderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "guideants_ci"),
                    ValueTemplate = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, collation: "guideants_ci"),
                    UserConfigPolicy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantAuthProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExcludedHosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcludedHosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileLineageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "guideants_ci"),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    FileKind = table.Column<int>(type: "integer", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, collation: "guideants_ci"),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileLineageEvents", x => x.Id);
                    table.CheckConstraint("CK_FileLineageEvent_NotebookId", "\"FileKind\" = 0 OR (\"FileKind\" = 1 AND \"NotebookId\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "JobQueue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "guideants_ci"),
                    PayloadJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalModelOperations",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "guideants_ci"),
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    RouterModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    ImmutableInputJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "guideants_ci"),
                    CurrentStep = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    CompletedSideEffectsJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, collation: "guideants_ci"),
                    Remediation = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, collation: "guideants_ci"),
                    DesiredRevision = table.Column<int>(type: "integer", nullable: true),
                    AppliedRevision = table.Column<int>(type: "integer", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalModelOperations", x => x.OperationId);
                });

            migrationBuilder.CreateTable(
                name: "Models",
                columns: table => new
                {
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "guideants_ci"),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true, collation: "guideants_ci"),
                    ContextWindowTokens = table.Column<int>(type: "integer", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: true),
                    ReasoningChoicesJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    RuntimeConfigJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    CombineSystemAndDeveloperMessages = table.Column<bool>(type: "boolean", nullable: false),
                    ThoughtBlockPattern = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, collation: "guideants_ci"),
                    SamplingParametersJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ThinkingControlJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    RequestFieldsWhenToolsPresentJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}", collation: "guideants_ci"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Models", x => x.ModelId);
                });

            migrationBuilder.CreateTable(
                name: "NotebookTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true, collation: "guideants_ci"),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "guideants_ci"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UserId = table.Column<string>(type: "character varying(450)", nullable: true, collation: "guideants_ci"),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotebookFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedGuideId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceChannel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    ExternalRequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    ExternalUserIdentity = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    NotebookConversationMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvokingAssistantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentInvocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Service = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    Operation = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ModelDeploymentId = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    ValueInput = table.Column<long>(type: "bigint", nullable: false),
                    ValueCachedInput = table.Column<long>(type: "bigint", nullable: false),
                    ValueReasoning = table.Column<long>(type: "bigint", nullable: false),
                    ValueOutput = table.Column<long>(type: "bigint", nullable: false),
                    ValueOther = table.Column<long>(type: "bigint", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    CostUsd = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    MarkupPercent = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    ChargeUsd = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageReportCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "guideants_ci"),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageReportCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Email = table.Column<string>(type: "character varying(450)", nullable: false, collation: "guideants_ci"),
                    IdentityIssuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    IdentitySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    PasswordHash = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    SecurityStamp = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PreferencesJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "guideants_ci"),
                    Locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AssistantAuthScopes",
                columns: table => new
                {
                    AssistantAuthProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantAuthScopes", x => new { x.AssistantAuthProviderId, x.Scope });
                    table.ForeignKey(
                        name: "FK_AssistantAuthScopes_AssistantAuthProviders_Assistan_868e3b97",
                        column: x => x.AssistantAuthProviderId,
                        principalTable: "AssistantAuthProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assistants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsGlobal = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true, collation: "guideants_ci"),
                    Instructions = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    Temperature = table.Column<float>(type: "real", nullable: true),
                    TopP = table.Column<double>(type: "double precision", nullable: true),
                    ReasoningEffort = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Model-specific reasoning effort value.", collation: "guideants_ci"),
                    SamplingParametersJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    ToolResourcesJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    MetadataJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    AuthConfigJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    SandboxWireApiConfigJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    DefaultEnvironmentConfigJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    DefaultAssistant = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    InvocationEvaluator = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    MaxToolCallsPerTurn = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<byte>(type: "smallint", nullable: false, comment: "AssistantKind discriminator"),
                    AvatarImageBytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    AvatarContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, collation: "guideants_ci"),
                    HomePageMarkdown = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assistants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assistants_Models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "Models",
                        principalColumn: "ModelId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LocalModelInstallations",
                columns: table => new
                {
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    ManagementMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "guideants_ci"),
                    CatalogId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    CatalogVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    Repository = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, collation: "guideants_ci"),
                    RequestedRevision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    ResolvedRevision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    QuantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    QuantLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    RouterModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    TargetDirectory = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, collation: "guideants_ci"),
                    ModelArtifactsJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ProjectorArtifactsJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    CompanionArtifactsJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    RouterPresetSnapshotJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalModelInstallations", x => x.ModelId);
                    table.ForeignKey(
                        name: "FK_LocalModelInstallations_Models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "Models",
                        principalColumn: "ModelId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsageReportCategoryOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    UsageReportCategoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageReportCategoryOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageReportCategoryOperations_UsageReportCategories_0b5ac5e3",
                        column: x => x.UsageReportCategoryId,
                        principalTable: "UsageReportCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CliAuthSessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    DeviceSecretHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CliAuthSessions", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_CliAuthSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OAuthAuthorizationStates",
                columns: table => new
                {
                    State = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    CodeVerifier = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, collation: "guideants_ci"),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "guideants_ci"),
                    Tenant = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "guideants_ci"),
                    Scopes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true, collation: "guideants_ci"),
                    RedirectUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, collation: "guideants_ci"),
                    ReturnUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAuthorizationStates", x => x.State);
                    table.ForeignKey(
                        name: "FK_OAuthAuthorizationStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantContextOptions",
                columns: table => new
                {
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantContextOptions", x => new { x.AssistantId, x.Key });
                    table.ForeignKey(
                        name: "FK_AssistantContextOptions_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantConversationStarters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prompt = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, collation: "guideants_ci"),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantConversationStarters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantConversationStarters_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "guideants_ci"),
                    VectorStoreName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    RelativePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false, collation: "guideants_ci"),
                    ContentBytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantFiles_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantOpenApiSchemas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    ApiHost = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    SpecificationJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    AuthProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantOpenApiSchemas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantOpenApiSchemas_AssistantAuthProviders_Auth_5414117c",
                        column: x => x.AuthProviderId,
                        principalTable: "AssistantAuthProviders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssistantOpenApiSchemas_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantSkillMetas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false, collation: "guideants_ci"),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantSkillMetas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantSkillMetas_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantTools",
                columns: table => new
                {
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantTools", x => new { x.AssistantId, x.ToolId });
                    table.ForeignKey(
                        name: "FK_AssistantTools_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssistantTools_Tools_ToolId",
                        column: x => x.ToolId,
                        principalTable: "Tools",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GuideMembers",
                columns: table => new
                {
                    GuideId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: true),
                    MaxToolCallsPerInvocation = table.Column<int>(type: "integer", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuideMembers", x => new { x.GuideId, x.AssistantId });
                    table.ForeignKey(
                        name: "FK_GuideMembers_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GuideMembers_Assistants_GuideId",
                        column: x => x.GuideId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantFileMarkdownShadows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAssistantFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "guideants_ci"),
                    StoragePath = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsIndexed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantFileMarkdownShadows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantFileMarkdownShadows_AssistantFiles_Origina_e4c40373",
                        column: x => x.OriginalAssistantFileId,
                        principalTable: "AssistantFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantOpenApiOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, collation: "guideants_ci"),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, collation: "guideants_ci"),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, collation: "guideants_ci"),
                    ToolDefinitionJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    SchemaFragmentJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantOpenApiOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantOpenApiOperations_AssistantOpenApiSchemas_SchemaId",
                        column: x => x.SchemaId,
                        principalTable: "AssistantOpenApiSchemas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentInvocationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentInvocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ToolCallsJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    FunctionName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    ToolCallId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentInvocationMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentInvocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentTurnIndex = table.Column<int>(type: "integer", nullable: false),
                    TriggeringToolCallId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "guideants_ci"),
                    ParentInvocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistantName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    ModelDeploymentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    Instructions = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ContextMessageJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    Evaluator = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, collation: "guideants_ci"),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    UsageJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    LlmRoundTrips = table.Column<int>(type: "integer", nullable: false),
                    ToolCallCount = table.Column<int>(type: "integer", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Completed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentInvocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentInvocations_AgentInvocations_ParentInvocationId",
                        column: x => x.ParentInvocationId,
                        principalTable: "AgentInvocations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgentInvocations_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContentFileMarkdownShadows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalContentFileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "guideants_ci"),
                    StoragePath = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsIndexed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentFileMarkdownShadows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Path = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    RelativePath = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    DocumentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestVersion = table.Column<int>(type: "integer", nullable: false),
                    IsSnapshot = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "guideants_ci"),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, collation: "guideants_ci"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystemProject = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HomePageContentFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_ContentFiles_HomePageContentFileId",
                        column: x => x.HomePageContentFileId,
                        principalTable: "ContentFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ExternalOAuthTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    AccessTokenEncrypted = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Scope = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalOAuthTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalOAuthTokens_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ExternalOAuthTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Links_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProjectAssistantEnvironments",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentConfigJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAssistantEnvironments", x => new { x.ProjectId, x.AssistantId });
                    table.ForeignKey(
                        name: "FK_ProjectAssistantEnvironments_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectAssistantEnvironments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectExternalAuths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    AuthType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "guideants_ci"),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "guideants_ci"),
                    Tenant = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "guideants_ci"),
                    HeaderName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    HeaderValue = table.Column<string>(type: "text", maxLength: 4096, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectExternalAuths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectExternalAuths_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    RelativePath = table.Column<string>(type: "character varying(450)", nullable: false, collation: "guideants_ci"),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectFolders_ProjectFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "ProjectFolders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectFolders_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SemiStructuredProjectDatas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Placeholder = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemiStructuredProjectDatas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemiStructuredProjectDatas_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserProjectContextOption",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, collation: "guideants_ci"),
                    Value = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProjectContextOption", x => new { x.UserId, x.ProjectId, x.Key });
                    table.ForeignKey(
                        name: "FK_UserProjectContextOption_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserProjectContextOption_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentFileVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    StoragePath = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    OriginalRelativePath = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    OriginalFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Path = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    RelativePath = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    Indexed = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    OriginVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromNotebook = table.Column<bool>(type: "boolean", nullable: false),
                    OriginNotebookId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginNotebookFileId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentFileVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentFileVersions_ContentFileVersions_OriginVersionId",
                        column: x => x.OriginVersionId,
                        principalTable: "ContentFileVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContentFileVersions_ContentFiles_ContentFileId",
                        column: x => x.ContentFileId,
                        principalTable: "ContentFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentFileVersions_ProjectFolders_OriginalFolderId",
                        column: x => x.OriginalFolderId,
                        principalTable: "ProjectFolders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConversationLocks",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockedByUserName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationLocks", x => x.ConversationId);
                    table.CheckConstraint("CK_ExpiresInFuture", "\"ExpiresAt\" > \"LockedAt\"");
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnIndex = table.Column<int>(type: "integer", nullable: false),
                    AssistantName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    ModelDeploymentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    Instructions = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ChatRunOutputJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    UsageJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    FilesCreated = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    FilesModified = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    WorkingDirectory = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "completed", collation: "guideants_ci"),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TerminalizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    TerminationDetail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "guideants_ci"),
                    CheckpointVersion = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurns", x => x.Id);
                    table.UniqueConstraint("AK_ConversationTurns_NotebookConversationId_TurnIndex", x => new { x.NotebookConversationId, x.TurnIndex });
                    table.CheckConstraint("CK_Turn_Status", "\"Status\" IN ('streaming', 'completed', 'cancelled', 'timed_out', 'failed', 'interrupted', 'pending_client_tool')");
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurnTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationTurnId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnIndex = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CaptureState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, collation: "guideants_ci"),
                    TraceJson = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurnTraces", x => x.Id);
                    table.CheckConstraint("CK_ConversationTurnTrace_State", "\"CaptureState\" IN ('partial', 'completed', 'cancelled', 'failed')");
                    table.ForeignKey(
                        name: "FK_ConversationTurnTraces_ConversationTurns_ConversationTurnId",
                        column: x => x.ConversationTurnId,
                        principalTable: "ConversationTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IndexName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    ContentFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotebookFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssistantFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_AssistantFiles_AssistantFileId",
                        column: x => x.AssistantFileId,
                        principalTable: "AssistantFiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentChunks_ContentFiles_ContentFileId",
                        column: x => x.ContentFileId,
                        principalTable: "ContentFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostFolderMountLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostFolderMountId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkRelativePath = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    LinkPhysicalPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false, collation: "guideants_ci"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastLinkedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true, collation: "guideants_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostFolderMountLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostFolderMounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: true),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    SourceKind = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    LeafName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    MountKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    SourceSpec = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    ContainerSourcePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, collation: "guideants_ci"),
                    NetworkDevice = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true, collation: "guideants_ci"),
                    NetworkOptions = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, collation: "guideants_ci"),
                    CredentialRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, collation: "guideants_ci"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    RemovedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true, collation: "guideants_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostFolderMounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostFolderMounts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostFolderMounts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MessageAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelativePath = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    UploadType = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MessageEditHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalContent = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    OriginalToolCalls = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    FirstEditedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstEditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageEditHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageEditHistories_Users_FirstEditedByUserId",
                        column: x => x.FirstEditedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NotebookConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalUserIdentity = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    AssistantName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    ModelDeploymentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    ThinkingBlocksJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    ToolCalls = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    FunctionName = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    ToolCallId = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    TurnIndex = table.Column<int>(type: "integer", nullable: false),
                    MessageSequence = table.Column<int>(type: "integer", nullable: false),
                    MessageContentType = table.Column<int>(type: "integer", nullable: false),
                    IsEdited = table.Column<bool>(type: "boolean", nullable: false),
                    LastEditedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastEditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsStreaming = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotebookConversationMessages_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NotebookConversationMessages_ConversationTurns_Note_3be5797c",
                        columns: x => new { x.NotebookConversationId, x.TurnIndex },
                        principalTable: "ConversationTurns",
                        principalColumns: new[] { "NotebookConversationId", "TurnIndex" });
                    table.ForeignKey(
                        name: "FK_NotebookConversationMessages_Users_LastEditedByUserId",
                        column: x => x.LastEditedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NotebookConversationMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NotebookConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Summary = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CompactionBoundaryTurnIndex = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotebookFileMarkdownShadows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalNotebookFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "guideants_ci"),
                    StoragePath = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsIndexed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookFileMarkdownShadows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotebookFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(450)", nullable: false, collation: "guideants_ci"),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FileHash = table.Column<string>(type: "text", nullable: false, collation: "guideants_ci"),
                    OriginContentFileVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    DocumentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotebookFiles_ContentFileVersions_OriginContentFileVersionId",
                        column: x => x.OriginContentFileVersionId,
                        principalTable: "ContentFileVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Notebooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "guideants_ci"),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, collation: "guideants_ci"),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuideId = table.Column<Guid>(type: "uuid", nullable: true),
                    HomePageFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    HomePageConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    SourceNotebookId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceConversationMessageId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notebooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notebooks_Assistants_GuideId",
                        column: x => x.GuideId,
                        principalTable: "Assistants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Notebooks_NotebookConversationMessages_SourceConver_1b7a6156",
                        column: x => x.SourceConversationMessageId,
                        principalTable: "NotebookConversationMessages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Notebooks_NotebookConversations_HomePageConversationId",
                        column: x => x.HomePageConversationId,
                        principalTable: "NotebookConversations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Notebooks_NotebookFiles_HomePageFileId",
                        column: x => x.HomePageFileId,
                        principalTable: "NotebookFiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Notebooks_Notebooks_SourceNotebookId",
                        column: x => x.SourceNotebookId,
                        principalTable: "Notebooks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Notebooks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NotebookLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotebookLinks_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotebookLinks_Notebooks_NotebookId",
                        column: x => x.NotebookId,
                        principalTable: "Notebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotebookSemiStructuredDatas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: false),
                    SemiStructuredProjectDataId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookSemiStructuredDatas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotebookSemiStructuredDatas_Notebooks_NotebookId",
                        column: x => x.NotebookId,
                        principalTable: "Notebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotebookSemiStructuredDatas_SemiStructuredProjectDa_ebcdcbd3",
                        column: x => x.SemiStructuredProjectDataId,
                        principalTable: "SemiStructuredProjectDatas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectScheduledJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "guideants_ci"),
                    JobType = table.Column<byte>(type: "smallint", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    TimeZoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "guideants_ci"),
                    ConversationTitle = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, collation: "guideants_ci"),
                    Prompt = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    AssistantName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "guideants_ci"),
                    ScriptNotebookFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextRunUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRunUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRunStatus = table.Column<byte>(type: "smallint", nullable: true),
                    ExposeSandboxWireApi = table.Column<bool>(type: "boolean", nullable: false),
                    WireTargetAssistantId = table.Column<Guid>(type: "uuid", nullable: true),
                    WireAttributionConversationTitle = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, collation: "guideants_ci"),
                    WireCreateAttributionConversationPerRun = table.Column<bool>(type: "boolean", nullable: false),
                    WireDailyLimitUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    WireMonthlyLimitUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectScheduledJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectScheduledJobs_NotebookFiles_ScriptNotebookFileId",
                        column: x => x.ScriptNotebookFileId,
                        principalTable: "NotebookFiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectScheduledJobs_Notebooks_NotebookId",
                        column: x => x.NotebookId,
                        principalTable: "Notebooks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectScheduledJobs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectScheduledJobs_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PublishedGuides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuideId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotebookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    AuthMode = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RetentionPeriod = table.Column<int>(type: "integer", nullable: true),
                    MaxUserMessageLength = table.Column<int>(type: "integer", nullable: true),
                    MaxTurns = table.Column<int>(type: "integer", nullable: true),
                    AuthValidationWebhookUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true, collation: "guideants_ci"),
                    AuthWebhookTimeoutSeconds = table.Column<int>(type: "integer", nullable: true),
                    FriendlyName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "guideants_ci"),
                    DisplayMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, collation: "guideants_ci"),
                    CommandMode = table.Column<bool>(type: "boolean", nullable: false),
                    ShowTurnNavigation = table.Column<bool>(type: "boolean", nullable: false),
                    Collapsible = table.Column<bool>(type: "boolean", nullable: false),
                    ShowConversationStarters = table.Column<bool>(type: "boolean", nullable: false),
                    ShowAttachments = table.Column<bool>(type: "boolean", nullable: false),
                    ShowSpeechToText = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    WireApiConfigJson = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    ApiKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "guideants_ci"),
                    McpEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    McpDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, collation: "guideants_ci"),
                    DailyChargeLimitUsd = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    BillingPeriodChargeLimitUsd = table.Column<decimal>(type: "numeric(19,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedGuides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishedGuides_Assistants_GuideId",
                        column: x => x.GuideId,
                        principalTable: "Assistants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PublishedGuides_Notebooks_NotebookId",
                        column: x => x.NotebookId,
                        principalTable: "Notebooks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProjectScheduledJobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggeredBy = table.Column<byte>(type: "smallint", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true, collation: "guideants_ci"),
                    StandardOutput = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    StandardError = table.Column<string>(type: "text", nullable: true, collation: "guideants_ci"),
                    CreatedConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectScheduledJobRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectScheduledJobRuns_ProjectScheduledJobs_ScheduledJobId",
                        column: x => x.ScheduledJobId,
                        principalTable: "ProjectScheduledJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Tools",
                columns: new[] { "Id", "Category", "Created", "Description", "DisplayName", "DisplayOrder", "IsActive", "ToolType", "Updated" },
                values: new object[,]
                {
                    { new Guid("b0000000-0000-0000-0000-000000000001"), "Search", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reads a web page URL and extracts requested content from it.", "Read Web", 1, true, "ReadWeb", null },
                    { new Guid("b0000000-0000-0000-0000-000000000002"), "Media", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Generate an image using AI.", "Generate Image", 1, true, "generate_image", null },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), "Media", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Generate a WAV podcast from SSML.", "Generate Podcast", 2, true, "generate_podcast", null },
                    { new Guid("b0000000-0000-0000-0000-000000000005"), "Media", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Edit or modify an existing notebook image using AI.", "Edit Image", 3, true, "MakeImageFromImage", null },
                    { new Guid("b0000000-0000-0000-0000-000000000007"), "Code", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Execute PlantUML to generate diagrams.", "Make Diagram", 1, true, "make_diagram", null },
                    { new Guid("b0000000-0000-0000-0000-000000000008"), "Code", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Execute bash in a container.", "Run Bash", 2, true, "run_bash", null },
                    { new Guid("b0000000-0000-0000-0000-000000000009"), "Code", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Execute Python in a container.", "Run Python", 3, true, "run_python", null },
                    { new Guid("b0000000-0000-0000-0000-00000000000a"), "Search", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Search inside the current notebook.", "Search Notebook", 2, true, "search_notebook", null },
                    { new Guid("b0000000-0000-0000-0000-00000000000b"), "Search", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Search inside the whole project.", "Search Project", 3, true, "search_project", null },
                    { new Guid("b0000000-0000-0000-0000-00000000000c"), "Integration", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Set user context options.", "Set Context Options", 1, true, "set_user_context_options", null },
                    { new Guid("b0000000-0000-0000-0000-00000000000d"), "Search", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Search the web using searXNG.", "Web Search", 0, true, "WebSearch", null },
                    { new Guid("b0000000-0000-0000-0000-00000000000e"), "Search", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Fetch a web page and convert it to markdown.", "Get Content From URL", 4, true, "GetContentFromUrl", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentInvocationMessages_AgentInvocationId_Sequence",
                table: "AgentInvocationMessages",
                columns: new[] { "AgentInvocationId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentInvocations_AssistantId",
                table: "AgentInvocations",
                column: "AssistantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentInvocations_Created",
                table: "AgentInvocations",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_AgentInvocations_ParentConversationId_ParentTurnIndex",
                table: "AgentInvocations",
                columns: new[] { "ParentConversationId", "ParentTurnIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentInvocations_ParentInvocationId",
                table: "AgentInvocations",
                column: "ParentInvocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantAuthProviders_ProviderId",
                table: "AssistantAuthProviders",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversationStarters_AssistantId_OrderIndex",
                table: "AssistantConversationStarters",
                columns: new[] { "AssistantId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantFileMarkdownShadows_OriginalAssistantFileId",
                table: "AssistantFileMarkdownShadows",
                column: "OriginalAssistantFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantFileMarkdownShadows_Status",
                table: "AssistantFileMarkdownShadows",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantFiles_AssistantId_RelativePath",
                table: "AssistantFiles",
                columns: new[] { "AssistantId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantOpenApiOperations_SchemaId_OperationId",
                table: "AssistantOpenApiOperations",
                columns: new[] { "SchemaId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantOpenApiSchemas_ApiHost",
                table: "AssistantOpenApiSchemas",
                column: "ApiHost");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantOpenApiSchemas_AssistantId_Name",
                table: "AssistantOpenApiSchemas",
                columns: new[] { "AssistantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantOpenApiSchemas_AuthProviderId",
                table: "AssistantOpenApiSchemas",
                column: "AuthProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Assistants_IsGlobal_Kind_Name",
                table: "Assistants",
                columns: new[] { "IsGlobal", "Kind", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Assistants_ModelId",
                table: "Assistants",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Assistants_Name",
                table: "Assistants",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantSkillMetas_AssistantId_SkillName",
                table: "AssistantSkillMetas",
                columns: new[] { "AssistantId", "SkillName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTools_ToolId",
                table: "AssistantTools",
                column: "ToolId");

            migrationBuilder.CreateIndex(
                name: "IX_CliAuthSessions_ExpiresAt",
                table: "CliAuthSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_CliAuthSessions_UserId",
                table: "CliAuthSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFileMarkdownShadows_OriginalContentFileVersionId",
                table: "ContentFileMarkdownShadows",
                column: "OriginalContentFileVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentFileMarkdownShadows_Status",
                table: "ContentFileMarkdownShadows",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFiles_FolderId",
                table: "ContentFiles",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFiles_ProjectId",
                table: "ContentFiles",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFileVersions_ContentFileId_VersionNumber",
                table: "ContentFileVersions",
                columns: new[] { "ContentFileId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentFileVersions_OriginalFolderId",
                table: "ContentFileVersions",
                column: "OriginalFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFileVersions_OriginNotebookFileId",
                table: "ContentFileVersions",
                column: "OriginNotebookFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFileVersions_OriginNotebookId",
                table: "ContentFileVersions",
                column: "OriginNotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFileVersions_OriginVersionId",
                table: "ContentFileVersions",
                column: "OriginVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationLocks_ExpiresAt",
                table: "ConversationLocks",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_NotebookConversationId_LastUpdated",
                table: "ConversationTurns",
                columns: new[] { "NotebookConversationId", "LastUpdated" })
                .Annotation("Npgsql:IndexInclude", new[] { "Status", "TurnIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_NotebookConversationId_TurnIndex",
                table: "ConversationTurns",
                columns: new[] { "NotebookConversationId", "TurnIndex" },
                unique: true)
                .Annotation("Npgsql:IndexInclude", new[] { "AssistantName" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnTraces_ConversationTurnId",
                table: "ConversationTurnTraces",
                column: "ConversationTurnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnTraces_NotebookConversationId_TurnIndex",
                table: "ConversationTurnTraces",
                columns: new[] { "NotebookConversationId", "TurnIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_AssistantFileId",
                table: "DocumentChunks",
                column: "AssistantFileId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ContentFileId_ChunkIndex",
                table: "DocumentChunks",
                columns: new[] { "ContentFileId", "ChunkIndex" },
                unique: true,
                filter: "\"ContentFileId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_IndexName",
                table: "DocumentChunks",
                column: "IndexName");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_NotebookFileId_ChunkIndex",
                table: "DocumentChunks",
                columns: new[] { "NotebookFileId", "ChunkIndex" },
                unique: true,
                filter: "\"NotebookFileId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ProjectId",
                table: "DocumentChunks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ProjectId_NotebookId",
                table: "DocumentChunks",
                columns: new[] { "ProjectId", "NotebookId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedHosts_Created",
                table: "ExcludedHosts",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedHosts_Host",
                table: "ExcludedHosts",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalOAuthTokens_ExpiresAt",
                table: "ExternalOAuthTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalOAuthTokens_ProjectId",
                table: "ExternalOAuthTokens",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalOAuthTokens_UserId_ProviderId_Unique",
                table: "ExternalOAuthTokens",
                columns: new[] { "UserId", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileLineageEvents_FileId_VersionNumber",
                table: "FileLineageEvents",
                columns: new[] { "FileId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_FileLineageEvents_ProjectId_Timestamp",
                table: "FileLineageEvents",
                columns: new[] { "ProjectId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_GuideMembers_AssistantId",
                table: "GuideMembers",
                column: "AssistantId");

            migrationBuilder.CreateIndex(
                name: "IX_GuideMembers_GuideId_DisplayOrder",
                table: "GuideMembers",
                columns: new[] { "GuideId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_HostFolderMountLinks_HostFolderMountId_NotebookId",
                table: "HostFolderMountLinks",
                columns: new[] { "HostFolderMountId", "NotebookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostFolderMountLinks_NotebookId_LinkRelativePath_Ac_e91c9fb7",
                table: "HostFolderMountLinks",
                columns: new[] { "NotebookId", "LinkRelativePath" },
                unique: true,
                filter: "\"Status\" IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_HostFolderMounts_CreatedByUserId",
                table: "HostFolderMounts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostFolderMounts_MountKey",
                table: "HostFolderMounts",
                column: "MountKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostFolderMounts_NotebookId",
                table: "HostFolderMounts",
                column: "NotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_HostFolderMounts_ProjectId_Status",
                table: "HostFolderMounts",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_Claiming",
                table: "JobQueue",
                columns: new[] { "Status", "AvailableAt", "Priority", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_CorrelationDedup",
                table: "JobQueue",
                columns: new[] { "CorrelationId", "JobType", "Status" },
                unique: true,
                filter: "\"CorrelationId\" IS NOT NULL AND \"Status\" IN (0, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_CorrelationId",
                table: "JobQueue",
                column: "CorrelationId",
                filter: "\"CorrelationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_LeaseCleanup",
                table: "JobQueue",
                columns: new[] { "Status", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_TypeClaiming",
                table: "JobQueue",
                columns: new[] { "JobType", "Status", "AvailableAt", "Priority", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_Links_ProjectId",
                table: "Links",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalModelOperations_ModelId",
                table: "LocalModelOperations",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalModelOperations_RouterModelId",
                table: "LocalModelOperations",
                column: "RouterModelId",
                filter: "\"RouterModelId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LocalModelOperations_Status_UpdatedUtc",
                table: "LocalModelOperations",
                columns: new[] { "Status", "UpdatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_MessageId_NotebookFileId",
                table: "MessageAttachments",
                columns: new[] { "MessageId", "NotebookFileId" },
                unique: true,
                filter: "\"NotebookFileId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_MessageId_OrderIndex",
                table: "MessageAttachments",
                columns: new[] { "MessageId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_NotebookFileId",
                table: "MessageAttachments",
                column: "NotebookFileId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEditHistories_FirstEditedByUserId",
                table: "MessageEditHistories",
                column: "FirstEditedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEditHistories_MessageId",
                table: "MessageEditHistories",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Models_IsActive_DisplayOrder",
                table: "Models",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_NotebookConversationMessages_AssistantId",
                table: "NotebookConversationMessages",
                column: "AssistantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookConversationMessages_LastEditedByUserId",
                table: "NotebookConversationMessages",
                column: "LastEditedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookConversationMessages_NotebookConversationId_041cff40",
                table: "NotebookConversationMessages",
                columns: new[] { "NotebookConversationId", "Role", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_NotebookConversationMessages_NotebookConversationId_44988180",
                table: "NotebookConversationMessages",
                columns: new[] { "NotebookConversationId", "TurnIndex", "MessageSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_NotebookConversationMessages_NotebookConversationId_Created",
                table: "NotebookConversationMessages",
                columns: new[] { "NotebookConversationId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_NotebookConversationMessages_UserId_NotebookConversationId",
                table: "NotebookConversationMessages",
                columns: new[] { "UserId", "NotebookConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotebookConversations_NotebookId",
                table: "NotebookConversations",
                column: "NotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookConversations_NotebookId_Created",
                table: "NotebookConversations",
                columns: new[] { "NotebookId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_NotebookFileMarkdownShadows_OriginalNotebookFileId",
                table: "NotebookFileMarkdownShadows",
                column: "OriginalNotebookFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotebookFileMarkdownShadows_Status",
                table: "NotebookFileMarkdownShadows",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookFiles_DocumentId",
                table: "NotebookFiles",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotebookFiles_NotebookId",
                table: "NotebookFiles",
                column: "NotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookFiles_OriginContentFileVersionId",
                table: "NotebookFiles",
                column: "OriginContentFileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookFiles_RelativePath_NotebookId",
                table: "NotebookFiles",
                columns: new[] { "RelativePath", "NotebookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotebookLinks_LinkId",
                table: "NotebookLinks",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookLinks_NotebookId",
                table: "NotebookLinks",
                column: "NotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_GuideId",
                table: "Notebooks",
                column: "GuideId");

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_HomePageConversationId",
                table: "Notebooks",
                column: "HomePageConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_HomePageFileId",
                table: "Notebooks",
                column: "HomePageFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_ProjectId",
                table: "Notebooks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_ProjectId_Slug_Unique",
                table: "Notebooks",
                columns: new[] { "ProjectId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_SourceConversationMessageId",
                table: "Notebooks",
                column: "SourceConversationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_SourceNotebookId",
                table: "Notebooks",
                column: "SourceNotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookSemiStructuredDatas_NotebookId",
                table: "NotebookSemiStructuredDatas",
                column: "NotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookSemiStructuredDatas_SemiStructuredProjectDataId",
                table: "NotebookSemiStructuredDatas",
                column: "SemiStructuredProjectDataId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationStates_ExpiresAt",
                table: "OAuthAuthorizationStates",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationStates_UserId_ProviderId",
                table: "OAuthAuthorizationStates",
                columns: new[] { "UserId", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssistantEnvironments_AssistantId",
                table: "ProjectAssistantEnvironments",
                column: "AssistantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectExternalAuths_ProjectId_ProviderId",
                table: "ProjectExternalAuths",
                columns: new[] { "ProjectId", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFolders_ParentFolderId",
                table: "ProjectFolders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFolders_ProjectId",
                table: "ProjectFolders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFolders_RelativePath_ProjectId",
                table: "ProjectFolders",
                columns: new[] { "RelativePath", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_HomePageContentFileId",
                table: "Projects",
                column: "HomePageContentFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Slug_Unique",
                table: "Projects",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectScheduledJobRuns_ScheduledJobId_StartedUtc",
                table: "ProjectScheduledJobRuns",
                columns: new[] { "ScheduledJobId", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectScheduledJobRuns_ScheduledJobId_Status",
                table: "ProjectScheduledJobRuns",
                columns: new[] { "ScheduledJobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectScheduledJobs_CreatedByUserId",
                table: "ProjectScheduledJobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectScheduledJobs_NotebookId",
                table: "ProjectScheduledJobs",
                column: "NotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectScheduledJobs_ProjectId_IsEnabled_NextRunUtc",
                table: "ProjectScheduledJobs",
                columns: new[] { "ProjectId", "IsEnabled", "NextRunUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectScheduledJobs_ProjectId_Name",
                table: "ProjectScheduledJobs",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectScheduledJobs_ScriptNotebookFileId",
                table: "ProjectScheduledJobs",
                column: "ScriptNotebookFileId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedGuides_Active_Created",
                table: "PublishedGuides",
                columns: new[] { "Active", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedGuides_AuthValidationWebhookUrl",
                table: "PublishedGuides",
                column: "AuthValidationWebhookUrl",
                filter: "\"AuthValidationWebhookUrl\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedGuides_FriendlyName",
                table: "PublishedGuides",
                column: "FriendlyName",
                unique: true,
                filter: "\"FriendlyName\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedGuides_GuideId",
                table: "PublishedGuides",
                column: "GuideId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedGuides_GuideId_Active",
                table: "PublishedGuides",
                columns: new[] { "GuideId", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedGuides_NotebookId",
                table: "PublishedGuides",
                column: "NotebookId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemiStructuredProjectDatas_ProjectId",
                table: "SemiStructuredProjectDatas",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Tools_Category_IsActive_DisplayOrder",
                table: "Tools",
                columns: new[] { "Category", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Tools_ToolType",
                table: "Tools",
                column: "ToolType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AgentInvocationId_Category",
                table: "UsageEvents",
                columns: new[] { "AgentInvocationId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AssistantId_Created",
                table: "UsageEvents",
                columns: new[] { "AssistantId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_Category_Created",
                table: "UsageEvents",
                columns: new[] { "Category", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_ConversationId_Created",
                table: "UsageEvents",
                columns: new[] { "ConversationId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_Created",
                table: "UsageEvents",
                column: "Created")
                .Annotation("Npgsql:IndexInclude", new[] { "ProjectId", "CostUsd", "MarkupPercent", "ChargeUsd" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_CrewUsage",
                table: "UsageEvents",
                column: "AgentInvocationId")
                .Annotation("Npgsql:IndexInclude", new[] { "Created", "Category", "ConversationId", "ValueInput", "ValueCachedInput", "ValueReasoning", "ValueOutput", "ChargeUsd" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_ExternalRequestId",
                table: "UsageEvents",
                column: "ExternalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_GuideInvocationUsage",
                table: "UsageEvents",
                columns: new[] { "InvokingAssistantId", "ProjectId", "Created" })
                .Annotation("Npgsql:IndexInclude", new[] { "Category", "Operation", "ConversationId", "ValueInput", "ValueCachedInput", "ValueReasoning", "ValueOutput", "ChargeUsd" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_GuideUsageReport",
                table: "UsageEvents",
                columns: new[] { "AssistantId", "ProjectId", "AgentInvocationId", "Created" })
                .Annotation("Npgsql:IndexInclude", new[] { "Category", "Operation", "ConversationId", "ValueInput", "ValueCachedInput", "ValueReasoning", "ValueOutput", "ChargeUsd" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_NotebookId_Created_ForCostLimits",
                table: "UsageEvents",
                columns: new[] { "NotebookId", "Created" })
                .Annotation("Npgsql:IndexInclude", new[] { "ChargeUsd" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_ProjectId_Created",
                table: "UsageEvents",
                columns: new[] { "ProjectId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_ProjectId_NotebookId_ForLastActivity",
                table: "UsageEvents",
                columns: new[] { "ProjectId", "NotebookId" })
                .Annotation("Npgsql:IndexInclude", new[] { "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_PublishedGuideId_Created",
                table: "UsageEvents",
                columns: new[] { "PublishedGuideId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_SourceChannel_Created",
                table: "UsageEvents",
                columns: new[] { "SourceChannel", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_UserId_ProjectId_NotebookId_ConversationId",
                table: "UsageEvents",
                columns: new[] { "UserId", "ProjectId", "NotebookId", "ConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageReportCategories_Key",
                table: "UsageReportCategories",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageReportCategoryOperations_Operation",
                table: "UsageReportCategoryOperations",
                column: "Operation");

            migrationBuilder.CreateIndex(
                name: "IX_UsageReportCategoryOperations_UsageReportCategoryId",
                table: "UsageReportCategoryOperations",
                column: "UsageReportCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProjectContextOption_ProjectId_Key",
                table: "UserProjectContextOption",
                columns: new[] { "ProjectId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_AssignedByUserId",
                table: "UserRoles",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_Unique",
                table: "UserRoles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ApprovedByUserId",
                table: "Users",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email_Unique",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Identity_Unique",
                table: "Users",
                columns: new[] { "IdentityIssuer", "IdentitySubject" },
                unique: true,
                filter: "\"IdentityIssuer\" IS NOT NULL AND \"IdentitySubject\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentInvocationMessages_AgentInvocations_AgentInvocationId",
                table: "AgentInvocationMessages",
                column: "AgentInvocationId",
                principalTable: "AgentInvocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentInvocations_NotebookConversations_ParentConversationId",
                table: "AgentInvocations",
                column: "ParentConversationId",
                principalTable: "NotebookConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentFileMarkdownShadows_ContentFileVersions_Orig_8ef4cb92",
                table: "ContentFileMarkdownShadows",
                column: "OriginalContentFileVersionId",
                principalTable: "ContentFileVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentFiles_ProjectFolders_FolderId",
                table: "ContentFiles",
                column: "FolderId",
                principalTable: "ProjectFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentFiles_Projects_ProjectId",
                table: "ContentFiles",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ContentFileVersions_NotebookFiles_OriginNotebookFileId",
                table: "ContentFileVersions",
                column: "OriginNotebookFileId",
                principalTable: "NotebookFiles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ContentFileVersions_Notebooks_OriginNotebookId",
                table: "ContentFileVersions",
                column: "OriginNotebookId",
                principalTable: "Notebooks",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationLocks_NotebookConversations_ConversationId",
                table: "ConversationLocks",
                column: "ConversationId",
                principalTable: "NotebookConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationTurns_NotebookConversations_NotebookCon_74ec85b5",
                table: "ConversationTurns",
                column: "NotebookConversationId",
                principalTable: "NotebookConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentChunks_NotebookFiles_NotebookFileId",
                table: "DocumentChunks",
                column: "NotebookFileId",
                principalTable: "NotebookFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HostFolderMountLinks_HostFolderMounts_HostFolderMountId",
                table: "HostFolderMountLinks",
                column: "HostFolderMountId",
                principalTable: "HostFolderMounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HostFolderMountLinks_Notebooks_NotebookId",
                table: "HostFolderMountLinks",
                column: "NotebookId",
                principalTable: "Notebooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HostFolderMounts_Notebooks_NotebookId",
                table: "HostFolderMounts",
                column: "NotebookId",
                principalTable: "Notebooks",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MessageAttachments_NotebookConversationMessages_MessageId",
                table: "MessageAttachments",
                column: "MessageId",
                principalTable: "NotebookConversationMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MessageAttachments_NotebookFiles_NotebookFileId",
                table: "MessageAttachments",
                column: "NotebookFileId",
                principalTable: "NotebookFiles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MessageEditHistories_NotebookConversationMessages_MessageId",
                table: "MessageEditHistories",
                column: "MessageId",
                principalTable: "NotebookConversationMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_NotebookConversationMessages_NotebookConversations__ae25bc99",
                table: "NotebookConversationMessages",
                column: "NotebookConversationId",
                principalTable: "NotebookConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_NotebookConversations_Notebooks_NotebookId",
                table: "NotebookConversations",
                column: "NotebookId",
                principalTable: "Notebooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_NotebookFileMarkdownShadows_NotebookFiles_OriginalN_78114594",
                table: "NotebookFileMarkdownShadows",
                column: "OriginalNotebookFileId",
                principalTable: "NotebookFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_NotebookFiles_Notebooks_NotebookId",
                table: "NotebookFiles",
                column: "NotebookId",
                principalTable: "Notebooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Hand-written: the model maps ConversationCurrentState to a view, which EF does not create.
            // Same definition as the SQL Server view (migration RemoveNotebookConversationModelDeploymentOverride).
            migrationBuilder.Sql(
                """
                CREATE VIEW "ConversationCurrentState" AS
                SELECT
                    c."Id",
                    c."NotebookId",
                    c."Title",
                    c."Summary",
                    c."Created",
                    "lastTurn"."AssistantName" AS "CurrentAssistantName",
                    "lastTurn"."ModelDeploymentId" AS "CurrentModelDeploymentId",
                    "lastTurn"."Instructions" AS "LastInstructions",
                    "lastTurn"."Created" AS "LastActivity",
                    "lastTurn"."TurnIndex" AS "CurrentTurnIndex",
                    "lastTurn"."FilesCreated" AS "LastTurnFilesCreated",
                    "lastTurn"."FilesModified" AS "LastTurnFilesModified"
                FROM "NotebookConversations" c
                LEFT JOIN (
                    SELECT
                        t1."NotebookConversationId",
                        t1."AssistantName",
                        t1."ModelDeploymentId",
                        t1."Instructions",
                        t1."Created",
                        t1."TurnIndex",
                        t1."FilesCreated",
                        t1."FilesModified"
                    FROM "ConversationTurns" t1
                    WHERE t1."TurnIndex" = (
                        SELECT MAX(t2."TurnIndex")
                        FROM "ConversationTurns" t2
                        WHERE t2."NotebookConversationId" = t1."NotebookConversationId"
                    )
                ) "lastTurn" ON c."Id" = "lastTurn"."NotebookConversationId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP VIEW IF EXISTS "ConversationCurrentState";""");

            migrationBuilder.DropForeignKey(
                name: "FK_NotebookConversationMessages_Assistants_AssistantId",
                table: "NotebookConversationMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Notebooks_Assistants_GuideId",
                table: "Notebooks");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationTurns_NotebookConversations_NotebookCon_74ec85b5",
                table: "ConversationTurns");

            migrationBuilder.DropForeignKey(
                name: "FK_NotebookConversationMessages_NotebookConversations__ae25bc99",
                table: "NotebookConversationMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Notebooks_NotebookConversations_HomePageConversationId",
                table: "Notebooks");

            migrationBuilder.DropForeignKey(
                name: "FK_NotebookConversationMessages_Users_LastEditedByUserId",
                table: "NotebookConversationMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_NotebookConversationMessages_Users_UserId",
                table: "NotebookConversationMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_NotebookFiles_ContentFileVersions_OriginContentFileVersionId",
                table: "NotebookFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentFiles_ProjectFolders_FolderId",
                table: "ContentFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentFiles_Projects_ProjectId",
                table: "ContentFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_Notebooks_Projects_ProjectId",
                table: "Notebooks");

            migrationBuilder.DropForeignKey(
                name: "FK_Notebooks_NotebookFiles_HomePageFileId",
                table: "Notebooks");

            migrationBuilder.DropTable(
                name: "AgentInvocationMessages");

            migrationBuilder.DropTable(
                name: "ApplicationSettings");

            migrationBuilder.DropTable(
                name: "AssistantAuthScopes");

            migrationBuilder.DropTable(
                name: "AssistantContextOptions");

            migrationBuilder.DropTable(
                name: "AssistantConversationStarters");

            migrationBuilder.DropTable(
                name: "AssistantFileMarkdownShadows");

            migrationBuilder.DropTable(
                name: "AssistantOpenApiOperations");

            migrationBuilder.DropTable(
                name: "AssistantSkillMetas");

            migrationBuilder.DropTable(
                name: "AssistantTools");

            migrationBuilder.DropTable(
                name: "CliAuthSessions");

            migrationBuilder.DropTable(
                name: "ContentFileMarkdownShadows");

            migrationBuilder.DropTable(
                name: "ConversationLocks");

            migrationBuilder.DropTable(
                name: "ConversationTurnTraces");

            migrationBuilder.DropTable(
                name: "DocumentChunks");

            migrationBuilder.DropTable(
                name: "ExcludedHosts");

            migrationBuilder.DropTable(
                name: "ExternalOAuthTokens");

            migrationBuilder.DropTable(
                name: "FileLineageEvents");

            migrationBuilder.DropTable(
                name: "GuideMembers");

            migrationBuilder.DropTable(
                name: "HostFolderMountLinks");

            migrationBuilder.DropTable(
                name: "JobQueue");

            migrationBuilder.DropTable(
                name: "LocalModelInstallations");

            migrationBuilder.DropTable(
                name: "LocalModelOperations");

            migrationBuilder.DropTable(
                name: "MessageAttachments");

            migrationBuilder.DropTable(
                name: "MessageEditHistories");

            migrationBuilder.DropTable(
                name: "NotebookFileMarkdownShadows");

            migrationBuilder.DropTable(
                name: "NotebookLinks");

            migrationBuilder.DropTable(
                name: "NotebookSemiStructuredDatas");

            migrationBuilder.DropTable(
                name: "NotebookTemplates");

            migrationBuilder.DropTable(
                name: "OAuthAuthorizationStates");

            migrationBuilder.DropTable(
                name: "ProjectAssistantEnvironments");

            migrationBuilder.DropTable(
                name: "ProjectExternalAuths");

            migrationBuilder.DropTable(
                name: "ProjectScheduledJobRuns");

            migrationBuilder.DropTable(
                name: "PublishedGuides");

            migrationBuilder.DropTable(
                name: "UsageEvents");

            migrationBuilder.DropTable(
                name: "UsageReportCategoryOperations");

            migrationBuilder.DropTable(
                name: "UserProjectContextOption");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "AgentInvocations");

            migrationBuilder.DropTable(
                name: "AssistantOpenApiSchemas");

            migrationBuilder.DropTable(
                name: "Tools");

            migrationBuilder.DropTable(
                name: "AssistantFiles");

            migrationBuilder.DropTable(
                name: "HostFolderMounts");

            migrationBuilder.DropTable(
                name: "Links");

            migrationBuilder.DropTable(
                name: "SemiStructuredProjectDatas");

            migrationBuilder.DropTable(
                name: "ProjectScheduledJobs");

            migrationBuilder.DropTable(
                name: "UsageReportCategories");

            migrationBuilder.DropTable(
                name: "AssistantAuthProviders");

            migrationBuilder.DropTable(
                name: "Assistants");

            migrationBuilder.DropTable(
                name: "Models");

            migrationBuilder.DropTable(
                name: "NotebookConversations");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "ContentFileVersions");

            migrationBuilder.DropTable(
                name: "ProjectFolders");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "ContentFiles");

            migrationBuilder.DropTable(
                name: "NotebookFiles");

            migrationBuilder.DropTable(
                name: "Notebooks");

            migrationBuilder.DropTable(
                name: "NotebookConversationMessages");

            migrationBuilder.DropTable(
                name: "ConversationTurns");
        }
    }
}
