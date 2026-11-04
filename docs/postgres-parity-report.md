# Postgres parity report

Can GuideAnts run on Postgres and behave the way it does on SQL Server? This report collects the evidence. It is
written for the supervisor and is filled in task by task, following
[the plan](superpowers/plans/2026-11-03-postgres-db-switch.md).

Status: **draft**. Rows marked `Open` are a working list, not findings. They get resolved (`Fixed` or `Accepted`) by
the task named in the row.

## Verdict

_To be written from the evidence (Task 18)._

## Evidence

_To be filled in (Task 18): the green CI runs for both providers, the production search comparison, and the speed
summary._

To run the integration suite on each provider, from the repository root:

```bash
# SQL Server (the default). Locally, use the GHCR image (amd64 only, emulated on Apple Silicon).
GA_INTEGRATION_TEST_MSSQL_IMAGE=ghcr.io/elumenotion/mssql2025-express-fts:main \
  dotnet test src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj

# Postgres. The image can be changed with GA_INTEGRATION_TEST_POSTGRES_IMAGE (default pgvector/pgvector:0.8.7-pg18).
GA_INTEGRATION_TEST_DB_PROVIDER=Postgres \
  dotnet test src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj
```

## Differences

These are the behavioral differences found between the two providers. Each resolution is one of:
- `Fixed`: Postgres now behaves like SQL Server.
- `Accepted`: the difference stays, and the row says why that is safe.
- `Open`: still being worked on.

| ID | Area | SQL Server behavior | Postgres default | Resolution | Proving test |
|---|---|---|---|---|---|
| 1 | String length of unbounded strings used in a key, FK or index (`NotebookFiles.RelativePath`, `ProjectFolders.RelativePath`, `UsageEvents.UserId`, `Users.Email`) | `nvarchar(450)`, because the type mapping bounds keys and indexes | `text` | **Fixed**: the adapter maps these to `character varying(450)` (900 if non-Unicode). As in EF's own mapping, the facets come from the principal key | `String_column_lengths_match_sql_server`, `Same_columns` |
| 2 | `[MaxLength(4096)]` `ProjectExternalAuths.HeaderValue` | `nvarchar(max)`: any length over 4000 becomes max | `character varying(4096)` | **Fixed**: the adapter maps a length over 4000 (8000 if non-Unicode) to `text` | `String_column_lengths_match_sql_server`, `Same_columns` |
| 3 | Delete action of the 32 `DeleteBehavior.Restrict` FKs | `NO ACTION` (SQL Server has no `RESTRICT`) | `RESTRICT` | **Fixed**: the adapter sets these FKs to `NO ACTION` | `Restrict_foreign_keys_use_no_action_like_sql_server`, `Same_foreign_keys` |
| 4 | Shape of `DocumentChunk.Embedding` in the migrations snapshot | n/a | primitive collection of `Vector`, so the snapshot fails to build | **Fixed**: the adapter maps it to a scalar pgvector column | `Embedding_maps_to_pgvector`, `Postgres_baseline_migrates_an_empty_database` |
| 5 | `ConversationCurrentState` view | created by raw SQL in the SQL Server migrations | missing, because EF doesn't create views | **Fixed**: the baseline has a hand-written `CREATE VIEW` with the same definition | `Same_views` |
| 6 | `ApplicationSettingsBackup_20260414_ProviderRouting` table | a one-time backup table created by migration 20260414183022; not in the model and never read | absent | **Accepted**: a new install has nothing to back up | `Same_tables` |
| 7 | Filter on `IX_ConversationTurnTraces_ConversationTurnId` | `WHERE [ConversationTurnId] IS NOT NULL`, from a migration without a Designer | no filter | **Accepted**: the column is NOT NULL, so the filter has no effect | `Same_indexes` |
| 8 | Names over 63 characters (3 indexes, 11 FKs) | the full name | shortened to 54 characters + `_` + 8 hex digits | **Accepted**: Postgres limits identifiers to 63 bytes | all `Same_*` schema parity tests |
| 9 | Seeded `ApplicationSettings` (14 default sections) | inserted by the T-SQL migrations 20260414183022 and 20260415112500 | none | **Fixed** in the baseline, with the same text values | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 10 | Seeded usage report categories (9) and operations (34) | inserted by the T-SQL migration 20260619195619 | none | **Fixed** in the baseline | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 11 | Ids of the rows in #10 | `NEWID()`, so they differ on every SQL Server database | n/a | **Accepted**: Postgres uses fixed ids. The code reads these rows by `Key`/`Operation`, never by id | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 12 | `STT`/`stt` and `TTS`/`tts` operations | the seed lists both spellings; the case-insensitive `DISTINCT` keeps one (observed: upper case) | n/a | **Fixed**: Postgres seeds `STT` and `TTS` only | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 13 | Rows in `ApplicationSettingsBackup_20260414_ProviderRouting` | 0 on a fresh database | table absent (#6) | **Accepted**: no rows are lost | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 14 | Row version granularity (`rowversion` becomes the `xmin` system column) | `rowversion` changes on every update statement | `xmin` changes once per transaction and is 32-bit | **Accepted**: two updates of one row in one transaction keep the same token. The concurrency check still compares the token read with the token at save time. Runtime proof is Task 9 | `Row_versions_map_to_xmin` |
| 15 | MARS savepoint warning | SqlClient-only: with MARS on, EF disables savepoints and warns | Npgsql has no MARS | **Accepted**: the test is SQL Server-only (`DatabaseTestProvider.RequireSqlServer()`) and has no Postgres counterpart. A Postgres failure that looks like MARS ("A command is already in progress") would be a Task 14 finding | `Repeated_streaming_writes_do_not_trigger_mars_savepoint_warning` (Inconclusive on Postgres) |
| 16 | DB-backed settings loader at host startup (`ApplicationSettingsConfigurationProvider`) | `SqlConnection`, `OBJECT_ID('dbo.ApplicationSettings')` | `Keyword not supported: 'host'`, so host startup aborts | **Fixed**: the loader takes the provider. On Postgres it uses `NpgsqlConnection`, checks `to_regclass('"ApplicationSettings"')` and reads `SELECT "SectionName", "JsonValue" FROM "ApplicationSettings"`; a missing table gives `Required table 'ApplicationSettings' was not found ...`. The SQL Server statements and messages are unchanged | `Settings_loader_reads_application_settings_rows`, `Settings_loader_fails_loudly_when_the_application_settings_table_is_missing`, `Load_OnPostgres_WhenServerIsUnreachable_NamesHostAndDatabase`, `Load_OnPostgres_WithInvalidConnectionString_ThrowsLoudStartupException`, `PutChatDefaults_ImmediatelyReflectsInNotebookChatReadiness` |
| 17 | Auth registration app lock (`EXEC sp_getapplock @Resource = N'GuideAnts.Auth.Register'`) | serializes registrations | `42601: syntax error at or near "EXEC"`, so `/api/auth/register` returns 500 | **Open**: owned by Task 10 (`DatabaseAppLock`) | 13 tests, diagnostic group A |
| 18 | FK violation detection in `DistributedConversationLockService` (`SqlException.Number == 547`) | locking a missing conversation gives 404 / `KeyNotFoundException` | `23503` isn't recognized, so the caller gets 409 or "Conversation is locked by another user" | **Open**: owned by Task 10 (`DatabaseErrors.IsForeignKeyViolation`) | 3 tests, diagnostic group B |
| 19 | Deleting a row whose cascaded child also references it through a `NO ACTION` FK (`UserRoles.UserId` is CASCADE, `UserRoles.AssignedByUserId` is NO ACTION; roles are self-assigned by `IntegrationTestCurrentUserService` and for the first admin in `AuthEndpoints.cs:76`) | one `DELETE FROM Users` deletes the user and the cascaded role (verified with a two-table repro in `mssql2025-express-fts`) | `23503 ... violates foreign key constraint "FK_UserRoles_Users_AssignedByUserId"`: the NO ACTION check runs before the cascade removes the referencing row (verified with the same repro in `pgvector:0.8.7-pg18`) | **Open**: owner Task 14. Affects production too (deleting a user whose role row references that same user). In the suite it makes `BaseEndpointTest` cleanup fail for every later class | 36 tests, diagnostic group C |
| 20 | Startup storage migration runners (`AsciiSlugNormalizationRunner`, `NamedStorageMigrationRunner`) | build their own context with `UseSqlServer` | `Keyword not supported: 'host'` | **Open**: owner Task 14 (no earlier task covers them) | 7 tests, diagnostic group D |
| 21 | Background job claim (`JobQueueService`, T-SQL `UPDATE ... TOP (1)` CTE) | claims jobs | `42601: syntax error at or near "."`, logged 26 times by `BackgroundJobProcessor` in the diagnostic run (no test failed on it there) | **Open**: owned by Task 10 (`JobQueueClaimSql.ClaimPostgres`) | none yet; Task 10 adds them |
| 22 | Usage report time series buckets (`UsageQueryService` owner and project summaries) | `EF.Functions.DateDiffDay/Week/Month` count the UTC midnights, Sundays and month starts crossed since the anchor | `DateDiff*` can't be translated, so the summary throws | **Fixed**: on Postgres the bucket index is `(Created.Date - anchor.Date).Days` for days, the difference of whole weeks since Sunday 1900-01-07 for weeks, and `(year diff) * 12 + month diff` for months. The SQL Server SQL is unchanged (the EF command log before and after is byte-identical) | `Day_buckets_split_at_utc_midnight`, `Week_buckets_split_on_sunday`, `Month_buckets_split_on_the_first` |

### Working list (Task 6 Postgres run, all `Open`)

Run: `GA_INTEGRATION_TEST_DB_PROVIDER=Postgres dotnet test src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj`
on 2026-11-04 with commit `test(db): run integration tests on SQL Server or Postgres`. Result: 271 tests: **6 passed, 264
failed, 1 skipped**. The suite runs to completion.

- Passed: the 6 `Settings.RoutingProblemDetailsShapeTests`, which don't start the host.
- Skipped: `CompactionLlamaCppEndToEndTests.RealLlamaCpp_Overflows_Compacts_Continues_AndOffersRecall`. It is Inconclusive
  without a llama server and is skipped on SQL Server too.
- Failed: all 264 fail in class or test initialization with the same error, `Failed to load DB-backed application
  settings from '<unparseable-connection-string>' ... Keyword not supported: 'host'` (#16).
- Class names are relative to `GuideAntsApi.IntegrationTests`.
- The MARS test (#15) is in this list only because the host failed to start in its `BaseTestInitialize`. Its own first
  line marks it Inconclusive.

<details>
<summary>All 264 failing tests, by class</summary>

- `Endpoints.AdminUsersEndpointsTests` (7): `Approve_AssignsRoleAndApproverAndRejectsInvalidInput`, `ApprovedUser_GainsApprovedAccessImmediately_WithoutReLogin`, `ChangeRole_DoesNotRevokeSession_NorForceReLogin`, `ChangeRole_UpdatesSingleRoleRow`, `LastAdminSafeguard_BlocksDeactivateAndDemote`, `NonAdminToken_ReturnsForbidden_ForAllAdminUserRoutes`, `SetPassword_UsesHasherAndInvalidatesPreviousToken`
- `Endpoints.AssistantAvatarEndpointTests` (3): `GetAvatar_ReturnsImage_WhenFileExists`, `GetAvatar_ReturnsNotFound_WhenAssistantDoesNotExist`, `GetAvatar_ReturnsNotFound_WhenAvatarFileDoesNotExist`
- `Endpoints.AssistantsEndpointsTests` (1): `Assistant_crud_round_trip_through_api`
- `Endpoints.AuthEndpointsTests` (5): `ChangePassword_UpdatesPasswordClearsMustChangeFlagAndRotatesSession`, `ChangePassword_WithWrongCurrentPassword_ReturnsBadRequest`, `ConcurrentInitialRegistrations_CreateExactlyOneAdmin`, `RegisterLoginAndMeFlow_MatchesBackendAuthRequirements`, `RegisterPendingUserThenAdminApprove_AssignsRoleAndUnlocksApprovedEndpoints`
- `Endpoints.AuthorizationMatrixEndpointsTests` (1): `RepresentativeEndpointGuards_MatchRoleMatrixAndToolbarSplit`
- `Endpoints.CliAuthEndpointsTests` (14): `Approve_AsNonAdmin_Returns403`, `Approve_WithoutAuthentication_Returns401`, `CreateSession_Anonymous_Returns200WithSessionFields`, `Deny_AsAdmin_Returns204_ThenTokenPoll_Returns403`, `Deny_AsNonAdmin_Returns403`, `Deny_UnknownSession_Returns404`, `Deny_WithoutAuthentication_Returns401`, `FullFlow_CreateApproveIssue_TokenAuthenticatesAgainstMe`, `GetToken_AfterConsumed_Returns410`, `GetToken_BeforeApproval_Returns202Pending`, `GetToken_NoDeviceSecretHeader_Returns401`, `GetToken_UnknownSessionId_Returns404`, `GetToken_WrongDeviceSecret_Returns401`, `Login_StillReturns200_AfterTokenWriteRemoved`
- `Endpoints.ConversationManagerIntegrationTests` (9): `ConversationApi_AssistantSwitchShouldNotStoreHandoffMessage`, `ConversationApi_NewConversationShouldHaveProperMessageOrdering`, `ConversationApi_SystemMessageShouldAppearBeforeUserMessage`, `ConversationCurrentState_View_ShouldReturnCorrectData`, `ConversationCurrentState_WithAssistantChange_ShouldReflectNewAssistant`, `ConversationCurrentState_WithNoTurns_ShouldReturnNullCurrentState`, `ConversationManager_ShouldPreserveMessageContext`, `ConversationManager_ShouldTrackTurnsCorrectly`, `LoadConversationAsync_ShouldLoadMessagesInChronologicalOrder`
- `Endpoints.FileLineageEndpointsTests` (3): `DownloadLineageEvent_ReturnsFileContent`, `GetFileHistory_ReturnsEvents`, `GetLineageEvent_ReturnsDto`
- `Endpoints.GuideUsageEndpointsTests` (3): `Conversation_invocations_endpoint_returns_tree_for_seeded_turns`, `Guide_usage_endpoints_return_aggregates_for_seeded_activity`, `Turn_trace_endpoint_returns_prompt_trace_for_turn`
- `Endpoints.GuidesEndpointsTests` (6): `ExportGuide_Requires_admin_authorization`, `ExportGuide_Returns_bad_request_for_missing_guide`, `ExportGuide_Returns_zip_with_manifest_behavior`, `GetGuides_Requires_admin_authorization`, `Guide_crud_round_trip_through_api`, `ValidateGuideRuntime_Returns_ok_for_cloud_model`
- `Endpoints.HostFolderMountEndpointsTests` (11): `ApplyCommand_ReturnsSanitizedCommandShape`, `CreateMount_InvalidLeafName_ReturnsBadRequest`, `CreateMount_NotebookScope_CreatesSingleLinkRow`, `CreateMount_ProjectScope_ReturnsPlanSection11Shape`, `CreateMount_WindowsStyleHostPath_AcceptedWhenApiRunsOnLinux`, `DeleteMount_RemovesRecord`, `InternalComposeOverridePlan_ReturnsScriptShape`, `ListAndGet_OmitSensitiveFieldsInSummary_IncludeInAdminDetail`, `NonAdmin_CreateMount_ReturnsForbidden`, `Reconcile_ReturnsCurrentStatus`, `RemoveCommand_ReturnsPlanSection11Shape`
- `Endpoints.HostFolderMountReconcileIntegrationTests` (5): `NewNotebookWithActiveProjectMount_GetsLinkSymlinkAndRegistry`, `NewNotebookWithSourceAbsent_LinkPendingRestartOrLinkErrorSurfaced`, `RemoveCommand_UnlinksSymlinksBeforeReturningCommand`, `RemoveFlow_ReconcileAfterSourceRemoved_MarksRemoved`, `StartupReconciliation_RecreatesMissingSymlinkForActiveMount`
- `Endpoints.LlamaAuthorizationEndpointsTests` (3): `LlamaRoutes_Admin_PassAuthorizationGate`, `LlamaRoutes_NonAdmin_Return403`, `LlamaRoutes_Unauthenticated_Return401`
- `Endpoints.NotebookConversationAssistantSwitchingTests` (6): `GetConversation_AfterAssistantSwitch_ReturnsCurrentAssistant`, `SendMessage_AssistantSwitch_AddsSystemMessagesAndPreservesContext`, `SendMessage_MultipleAssistantSwitches_MaintainsCorrectContext`, `SendMessage_NewConversation_CreatesConversationWithSelectedAssistant`, `SendMessage_SameAssistantContinuation_DoesNotAddSystemMessages`, `SendMessage_StreamingWithAssistantSwitch_ReturnsCorrectEvents`
- `Endpoints.NotebookConversationEndpointsTests` (2): `CreateConversation_And_SendMessage_Flow`, `ListConversations_ReturnsLastActivity_AndOrdersByRecent`
- `Endpoints.NotebookConversationStreamingEndpointsTests` (2): `SendMessage_Stream_ReturnsSseEventsInOrder`, `SendMessage_Stream_WhenConversationIsLocked_Returns409Conflict`
- `Endpoints.NotebookConversationStreamingToolCallsTests` (3): `SendMessage_StreamWithSimpleAssistant_DoesNotContainToolCallEvents`, `SendMessage_StreamWithToolCalls_ContainsExpectedEventData`, `SendMessage_StreamWithToolCalls_ReturnsSseEventsInCorrectOrder`
- `Endpoints.NotebookConversationsEndpointsDeepTests` (14): `CreateConversation_WithSourceConversationId_CopiesMessages`, `EditMessage_ForAssistantMessage_Succeeds`, `EditMessage_ForMissingMessage_Returns404`, `EditMessage_ForUserMessage_Returns400`, `GenerateTitle_ForMissingConversation_Returns404`, `GetConversation_ForMissing_Returns404_and_RenameDelete_Succeed`, `SaveAs_CreatesMarkdownFile_FromConversation`, `SaveAs_ForMissingConversation_Returns404`, `SendMessage_Stream_ForMissingConversation_Returns404`, `SendMessage_WithoutEventStreamAccept_Returns400`, `UndoLast_ForMissingConversation_Returns404`, `UndoLast_RemovesLatestTurn`, `UndoSpecific_ForMissingMessage_Returns404`, `UndoSpecific_RemovesFromTargetTurn`
- `Endpoints.NotebookFileSyncEndpointsTests` (3): `Sync_adds_updates_and_deletes_files`, `Sync_concurrent_runs_do_not_duplicate`, `Sync_skips_locked_file_then_picks_up`
- `Endpoints.NotebookTemplateEndpointsTests` (2): `GetNotebookTemplateAvatar_ReturnsImage`, `GetNotebookTemplates_ReturnsTemplatesWithExpectedFields`
- `Endpoints.ProjectContentFileVersioningEndpointsTests` (4): `GetVersionContent_ReturnsCorrectContent`, `GetVersions_ReturnsAllVersions`, `UploadFile_FirstVersion_CreatesVersion1`, `UploadFile_NewVersion_IncrementsVersion`
- `Endpoints.ProjectContentFileWithFoldersEndpointsTests` (8): `MoveFile_DuplicateNameInFolder_ReturnsBadRequest`, `MoveFile_NonExistentFile_ReturnsNotFound`, `MoveFile_ToFolder_UpdatesFileLocation`, `MoveFile_ToInvalidFolder_ReturnsBadRequest`, `MoveFile_ToRoot_UpdatesFileLocation`, `UploadFile_WithFolderId_PlacesFileInFolder`, `UploadFile_WithInvalidFolderId_ReturnsBadRequest`, `UploadFile_WithoutFolderId_PlacesFileInRoot`
- `Endpoints.ProjectEndpointsTests` (8): `CopyProject_CreatesNewProjectWithSameContent`, `CreateProject_WithValidData_ReturnsCreatedProject`, `DeleteProject_WhenOwnerAndEmpty_RemovesProject`, `GetProjectDetails_ReturnsFullProjectDetails`, `GetProject_WhenExists_ReturnsProject`, `GetProject_WhenNotExists_ReturnsNotFound`, `GetUserProjects_ReturnsOnlyUserProjects`, `UpdateProject_WhenOwner_UpdatesProject`
- `Endpoints.ProjectFolderEndpointsTests` (13): `CreateFolder_WithDuplicatePath_ReturnsBadRequest`, `CreateFolder_WithInvalidName_ReturnsBadRequest`, `CreateFolder_WithParent_ReturnsCreatedWithCorrectPath`, `CreateFolder_WithValidData_ReturnsCreated`, `DeleteFolder_EmptyFolder_ReturnsNoContent`, `DeleteFolder_NonEmptyFolder_ReturnsBadRequest`, `GetFolderTree_WithNestedStructure_ReturnsCorrectTree`, `GetFolder_ExistingFolder_ReturnsOk`, `GetFolder_NonExistentFolder_ReturnsNotFound`, `GetFolders_ReturnsAllFolders`, `MoveFolder_CircularReference_ReturnsBadRequest`, `MoveFolder_ValidMove_ReturnsNoContent`, `UpdateFolder_WithNewName_ReturnsOk`
- `Endpoints.ProjectScheduledJobEndpointsTests` (7): `CreateListUpdateDelete_NewConversationJob_SucceedsForAdmin`, `GetJob_ReturnsNotFoundWhenJobBelongsToDifferentProject`, `GetRun_ReturnsNotFoundWhenJobBelongsToDifferentProject`, `ListRuns_ClampsPagingBounds`, `ListRuns_ReturnsNotFoundWhenJobBelongsToDifferentProject`, `RunNow_ReturnsAccepted`, `ScheduledJobEndpoints_RequireAdminRole`
- `Endpoints.PublishedGuidesAppIdentityEndpointsTests` (6): `AppIdentity_missing_token_on_message_endpoint_returns_401`, `GetPublishedGuide_Returns_authMode_and_requiresAuth_for_app_identity`, `PublishGuide_Rejects_app_identity_auth_mode`, `SendMessageStream_Persists_internal_user_id_for_app_identity`, `UpdatePublishedGuide_Rejects_changing_auth_mode_away_from_app_identity`, `UpdatePublishedGuide_Rejects_setting_auth_mode_to_app_identity`
- `Endpoints.PublishedGuidesEndpointsTests` (1): `GetPublishedGuide_Returns_not_found_for_unknown_id`
- `Endpoints.SandboxOpenAiWireEndpointsTests` (3): `GetModels_With_invalid_token_returns_401`, `GetModels_With_valid_sandbox_wire_jwt_returns_enabled_aliases`, `PostResponses_With_valid_token_returns_unsupported_feature`
- `Endpoints.SystemGuideEndpointsTests` (9): `GetProject_system_project_as_admin_returns_ok`, `GetProject_system_project_as_reader_returns_not_found`, `GetProjects_excludes_system_project`, `GetSandboxAdminRequirements_notebook_without_guide_returns_bad_request`, `GetSystemGuideSession_admin_returns_admin_published_guide`, `GetSystemGuideSession_contributor_returns_user_published_guide`, `GetSystemGuideSession_pending_returns_forbidden`, `GetSystemGuideWorkspace_admin_returns_project_slug`, `GetSystemGuideWorkspace_contributor_returns_not_found`
- `Endpoints.UsageEndpointsTests` (1): `Usage_summary_by_project_details_and_breakdown_return_data`
- `OpenApi.GuideantsSwaggerExportTests` (1): `ExportGuideantsSwagger_FromRunningApi`
- `Services.Conversations.AttachmentMessageBuilderIntegrationTests` (2): `CreateMessages_and_content_inline_real_text_file_from_file_service`, `CreateMessages_returns_empty_when_file_service_is_null`
- `Services.Conversations.CompactionEndToEndTests` (3): `Compact_WithoutPriorOverflow_FirstSendAfterCompaction_UsesSummary`, `CompactedConversation_StillOverflowing_FailsCleanly_WithoutMutatingContent`, `LongConversation_Overflows_Compacts_Continues_AndRecallsWhatTheSummaryReplaced`
- `Services.Conversations.ConversationPersistenceMarsSqlIntegrationTests` (1): `Repeated_streaming_writes_do_not_trigger_mars_savepoint_warning`
- `Services.Conversations.ConversationServiceIntegrationTests` (36): `CreateConversation_Throws_when_notebook_missing`, `CreateConversation_Trims_title_and_defaults_untitled`, `DeleteConversation_NoOp_when_missing`, `DeleteConversation_Removes_conversation_and_messages`, `EditMessage_endpoint_rejects_non_assistant_message`, `EditMessage_endpoint_returns_404_for_missing_message`, `EditMessage_endpoint_updates_assistant_message_and_records_history`, `GetConversationById_Projects_messages_with_edit_history_user_and_attachments`, `GetConversationById_ReturnsNull_WhenMissing`, `GetConversationWithMessages_Projects_turn_files_thinking_tool_calls_and_filters_duplicates`, `GetConversationWithMessages_ReturnsNull_WhenMissing`, `GetList_Orders_by_usage_event_last_activity`, `GetList_ReturnsEmpty_WhenNotebookMissing`, `GetUserConversations_excludes_deleted_projects_and_applies_search_sort_paging`, `ObserveEvents_unknown_conversation_returns_404`, `RenameConversation_Throws_when_missing`, `RenameConversation_Updates_and_keeps_existing_when_blank`, `SendMessageStream_Cancel_during_thinking_persists_thinking_without_zero_token_usage`, `SendMessageStream_Cancel_finalizes_partial_message_marks_turn_cancelled_and_prunes_incomplete_tool_calls`, `SendMessageStream_Checkpoints_thinking_before_terminalization`, `SendMessageStream_Complete_event_is_emitted_after_unlock_for_stream_client_and_observers`, `SendMessageStream_Emits_thinking_blocks_in_stream_and_persists_to_assistant_message`, `SendMessageStream_HttpCancel_during_nestedReadWeb_stops_worker_before_success_and_preserves_data`, `SendMessageStream_Persists_tool_call_assistant_and_tool_result_with_idempotent_tool_usage`, `SendMessageStream_Persists_turn_messages_usage_and_releases_lock`, `SendMessageStream_SseDisconnect_keeps_worker_locked_until_explicit_cancel_and_reattach_observe`, `SendMessageStream_Stop_wins_when_provider_ignores_cancellation_until_return`, `SendMessageStream_TimeoutDuringStream_preserves_partial_text_and_marks_turn_timed_out`, `SendMessageStream_With_attachment_persists_MessageAttachment_and_includes_file_in_history`, `SendMessageStream_With_duplicate_file_and_path_attachments_persists_each_canonical_attachment_once`, `SendMessageStream_With_multiple_attachments_persists_distinct_order_indexes`, `UndoForMessage_Removes_from_target_turn_onward`, `UndoForMessage_Throws_when_message_missing`, `UndoLast_NoOp_when_no_user_messages`, `UndoLast_Removes_only_latest_turn`, `UndoLast_Throws_when_conversation_missing`
- `Services.Conversations.ConversationStopDataPreservationSqlIntegrationTests` (1): `Stop_preserves_partial_thinking_tool_calls_and_cancellation_result_on_sql_with_mars`
- `Services.Conversations.ConversationTurnRecoverySqlIntegrationTests` (3): `RecoverStaleTurns_terminalizes_orphaned_streaming_turn_on_sql`, `TryClaimStaleTurnAsync_loses_when_checkpoint_advances_last_updated`, `TryClaimStaleTurnAsync_only_one_concurrent_claim_succeeds`
- `Services.Conversations.PublishedConversationModelResolutionTests` (1): `SendMessageStream_Resolves_via_global_override_when_assistant_has_no_model`
- `Services.Conversations.PublishedConversationServiceIntegrationTests` (8): `CreateConversation_Creates_with_trim_and_untitled_defaults`, `CreateConversation_Throws_when_notebook_missing`, `GetConversationWithMessages_Filters_duplicate_assistant_without_tool_calls`, `GetConversationWithMessages_Projects_user_join_edit_history_and_attachments`, `GetConversationWithMessages_Returns_null_for_missing`, `UndoLast_NoOp_when_conversation_missing`, `UndoLast_NoOp_when_no_user_messages`, `UndoLast_Removes_only_latest_turn`
- `Services.Conversations.PublishedConversationStreamingTests` (10): `ResumeAfterExternalToolResultsStream_Continues_run_and_persists_new_assistant_message`, `ResumeAfterExternalToolResults_WithSecondClientToolRound_ReleasesLockOnEachExternalToolCall`, `SendMessageStream_Enforces_MaxTurns_from_published_guide`, `SendMessageStream_Enforces_MaxUserMessageLength_from_published_guide`, `SendMessageStream_Persists_user_and_assistant_messages_and_streams_content`, `SendMessageStream_Throws_ArgumentException_when_instructions_blank_and_no_attachments`, `SendMessageStream_Throws_KeyNotFound_when_conversation_missing`, `SendMessageStream_When_client_tool_is_requested_leaves_turn_streaming`, `SendMessageStream_WithExternalToolCall_ReleasesLock_AllowingResumeWhileStreamOpen`, `SendMessageStream_With_path_only_folder_attachment_persists_and_injects_folder_notice`
- `Services.Conversations.ToolLimitIntegrationTests` (3): `SendMessageStream_RepeatedToolRounds_Persists_each_assistant_tool_call_message`, `SendMessageStream_ToolLimit_AllowsMaxThenSyntheticResult_AndEscalatesToForceComplete`, `SendMessageStream_ToolLimit_CompletedTurn_RehydratesOnGetReload_T13`
- `Services.Migrations.AsciiSlugNormalizationRunnerIntegrationTests` (3): `RunAsync_Apply_AllSlugsSafe_NoChangesRequired`, `RunAsync_Apply_NormalizesUnsafeSlugsAndMovesDirectories`, `RunAsync_DryRun_DoesNotMoveDirectoriesOrChangeSlugs`
- `Services.Migrations.NamedStorageMigrationRunnerIntegrationTests` (4): `RunAsync_Apply_ArchivesUnmappedLegacyGuidDirectories`, `RunAsync_Apply_MovesGuidDirectoriesToSlugsAndRewritesDbPaths`, `RunAsync_Apply_TargetSlugDirectoryAlreadyExists_ThrowsAndRollsBack`, `RunAsync_DryRun_DoesNotMoveDirectoriesOrChangeDb`
- `Settings.ChatDefaultsChatReadinessIntegrationTests` (1): `PutChatDefaults_ImmediatelyReflectsInNotebookChatReadiness`
- `Settings.ImageBundleDefinitionIntegrationTests` (2): `ExportImageGenerationBundleDefinition_ReturnsCanonicalJson`, `GetImageGenerationBundleDefinitions_ReturnsCheckedInDefaults`
- `Settings.Qwen36ChatWalkthroughTests` (9): `AddCatalogAndRouterAlias_InventoryReportsAlias_Unloaded`, `Bootstrap_Empty_InventoryReturnsEmpty`, `DispatchChat_BlankProvider_ThrowsProviderNotReady`, `DispatchChat_LlamaCppWithoutRuntimeConfigJson_ThrowsModelNotReady`, `DispatchChat_UnknownModel_ThrowsModelNotReady`, `DispatchChat_UnloadedRuntime_StillBuildsClient_R12_6`, `DispatchChat_WithLoadedRuntime_ReturnsLlamaCppClient`, `LoadAlias_TransitionsToLoaded_ViaSettingsEndpoint`, `UnloadAfterLoad_FollowedByChat_StillBuildsClient`
- `Settings.RoutingEndpointsTests` (4): `ChatTargetReadiness_Strict_BlockedModel_Returns409RoutingModelNotReady`, `ChatTargetReadiness_UnknownModel_Returns404WithModelNotFoundProblem`, `ChatTargetsPreflight_NoAssistantsConfigured_ReturnsEmptyList`, `GetChatTargets_ListsCatalogModels`
- `Settings.SettingsEndpointsDeepTests` (9): `ChatDefaults_GetAndPut_ReturnOk`, `GetSection_Unknown_Returns404`, `Infrastructure_Dependencies_Probes_And_Override`, `Models_FullCrudLifecycle`, `Overview_And_ConnectionUsage_ReturnOk`, `Sections_Schema_Readiness_ReturnOk`, `ServiceEditor_UnknownService_Returns400`, `UpdateSection_UnknownSection_Returns404`, `UpdateSection_ValidStaleAndUnsupportedField_CoverBranches`

</details>

#### Next layer, from a diagnostic run (not committed)

Every failure in the run above has the same cause, so I ran the suite once more with a throwaway patch:
`ApplicationSettingsConfigurationProvider` read the table through `NpgsqlConnection` on Postgres. I reverted the patch
right after the run, and it is not in any commit. The result was **210 passed, 59 failed, 2 skipped**. The second
skipped test is the MARS test, now Inconclusive as intended. The 59 failures are below.

These groups predict what will be left after Task 11. Group C also hides the real results of the 36 tests it blocks.

**A. Auth app lock, #17 (13):**
- `Endpoints.AdminUsersEndpointsTests` (7): `Approve_AssignsRoleAndApproverAndRejectsInvalidInput`, `ApprovedUser_GainsApprovedAccessImmediately_WithoutReLogin`, `ChangeRole_DoesNotRevokeSession_NorForceReLogin`, `ChangeRole_UpdatesSingleRoleRow`, `LastAdminSafeguard_BlocksDeactivateAndDemote`, `NonAdminToken_ReturnsForbidden_ForAllAdminUserRoutes`, `SetPassword_UsesHasherAndInvalidatesPreviousToken`
- `Endpoints.AuthEndpointsTests` (5): `ChangePassword_UpdatesPasswordClearsMustChangeFlagAndRotatesSession`, `ChangePassword_WithWrongCurrentPassword_ReturnsBadRequest`, `ConcurrentInitialRegistrations_CreateExactlyOneAdmin`, `RegisterLoginAndMeFlow_MatchesBackendAuthRequirements`, `RegisterPendingUserThenAdminApprove_AssignsRoleAndUnlocksApprovedEndpoints`
- `Endpoints.CliAuthEndpointsTests` (1): `Login_StillReturns200_AfterTokenWriteRemoved`

**B. Conversation lock FK detection, #18 (3):**
- `Endpoints.NotebookConversationsEndpointsDeepTests` (2): `SendMessage_Stream_ForMissingConversation_Returns404`, `UndoLast_ForMissingConversation_Returns404`
- `Services.Conversations.ConversationServiceIntegrationTests` (1): `UndoLast_Throws_when_conversation_missing`

**C. Self-referencing NO ACTION + CASCADE delete in cleanup, #19 (36).** Every `BaseEndpointTest` class that runs after
the first test user with a self-assigned role fails in `BaseTestInitialize`:
- `Endpoints.SystemGuideEndpointsTests` (9): `GetProject_system_project_as_admin_returns_ok`, `GetProject_system_project_as_reader_returns_not_found`, `GetProjects_excludes_system_project`, `GetSandboxAdminRequirements_notebook_without_guide_returns_bad_request`, `GetSystemGuideSession_admin_returns_admin_published_guide`, `GetSystemGuideSession_contributor_returns_user_published_guide`, `GetSystemGuideSession_pending_returns_forbidden`, `GetSystemGuideWorkspace_admin_returns_project_slug`, `GetSystemGuideWorkspace_contributor_returns_not_found`
- `Endpoints.UsageEndpointsTests` (1): `Usage_summary_by_project_details_and_breakdown_return_data`
- `Services.Conversations.ConversationStopDataPreservationSqlIntegrationTests` (1): `Stop_preserves_partial_thinking_tool_calls_and_cancellation_result_on_sql_with_mars`
- `Services.Conversations.ConversationTurnRecoverySqlIntegrationTests` (3): `RecoverStaleTurns_terminalizes_orphaned_streaming_turn_on_sql`, `TryClaimStaleTurnAsync_loses_when_checkpoint_advances_last_updated`, `TryClaimStaleTurnAsync_only_one_concurrent_claim_succeeds`
- `Services.Conversations.PublishedConversationModelResolutionTests` (1): `SendMessageStream_Resolves_via_global_override_when_assistant_has_no_model`
- `Services.Conversations.PublishedConversationServiceIntegrationTests` (8): `CreateConversation_Creates_with_trim_and_untitled_defaults`, `CreateConversation_Throws_when_notebook_missing`, `GetConversationWithMessages_Filters_duplicate_assistant_without_tool_calls`, `GetConversationWithMessages_Projects_user_join_edit_history_and_attachments`, `GetConversationWithMessages_Returns_null_for_missing`, `UndoLast_NoOp_when_conversation_missing`, `UndoLast_NoOp_when_no_user_messages`, `UndoLast_Removes_only_latest_turn`
- `Services.Conversations.PublishedConversationStreamingTests` (10): `ResumeAfterExternalToolResultsStream_Continues_run_and_persists_new_assistant_message`, `ResumeAfterExternalToolResults_WithSecondClientToolRound_ReleasesLockOnEachExternalToolCall`, `SendMessageStream_Enforces_MaxTurns_from_published_guide`, `SendMessageStream_Enforces_MaxUserMessageLength_from_published_guide`, `SendMessageStream_Persists_user_and_assistant_messages_and_streams_content`, `SendMessageStream_Throws_ArgumentException_when_instructions_blank_and_no_attachments`, `SendMessageStream_Throws_KeyNotFound_when_conversation_missing`, `SendMessageStream_When_client_tool_is_requested_leaves_turn_streaming`, `SendMessageStream_WithExternalToolCall_ReleasesLock_AllowingResumeWhileStreamOpen`, `SendMessageStream_With_path_only_folder_attachment_persists_and_injects_folder_notice`
- `Services.Conversations.ToolLimitIntegrationTests` (3): `SendMessageStream_RepeatedToolRounds_Persists_each_assistant_tool_call_message`, `SendMessageStream_ToolLimit_AllowsMaxThenSyntheticResult_AndEscalatesToForceComplete`, `SendMessageStream_ToolLimit_CompletedTurn_RehydratesOnGetReload_T13`

**D. SQL Server-only storage migration runners, #20 (7):**
- `Services.Migrations.AsciiSlugNormalizationRunnerIntegrationTests` (3): `RunAsync_Apply_AllSlugsSafe_NoChangesRequired`, `RunAsync_Apply_NormalizesUnsafeSlugsAndMovesDirectories`, `RunAsync_DryRun_DoesNotMoveDirectoriesOrChangeSlugs`
- `Services.Migrations.NamedStorageMigrationRunnerIntegrationTests` (4): `RunAsync_Apply_ArchivesUnmappedLegacyGuidDirectories`, `RunAsync_Apply_MovesGuidDirectoriesToSlugsAndRewritesDbPaths`, `RunAsync_Apply_TargetSlugDirectoryAlreadyExists_ThrowsAndRollsBack`, `RunAsync_DryRun_DoesNotMoveDirectoriesOrChangeDb`

#### After Task 11

Run on 2026-11-04 with commit `feat(db): load DB-backed settings and bucket usage on Postgres`. Result: 276 tests (the
271 above plus 5 new `DatabaseBehavior` tests): **215 passed, 59 failed, 2 skipped**. The host now starts on Postgres
(#16 fixed). Every test has the same outcome as in the diagnostic run above, and the 5 new tests pass, so the 59
failures are exactly groups A to D above (13 + 3 + 36 + 7). The 2 skipped tests are the llama test and the MARS test
(Inconclusive). `UsageEndpointsTests.Usage_summary_by_project_details_and_breakdown_return_data` still fails in its
initialization from #19 (group C), so it doesn't yet exercise the #22 fix through the endpoint; the `DatabaseBehavior`
bucket tests do.

## Intentional differences

_To be filled in (Task 18)._

## Ongoing cost

_To be filled in (Task 18): dual migrations, plus the extra CI minutes measured from the Task 15 runs._

## Limits

_To be filled in (Task 18)._ At minimum:
- x86 CI hardware only
- synthetic data volumes
- no data migration
- Guid and string tie ordering

## Speed

_To be filled in (Task 17)._
