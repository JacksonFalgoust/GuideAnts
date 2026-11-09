using FluentAssertions;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GuideAntsApi.Tests.DataModel;

/// <summary>
/// The Postgres shapes of <see cref="DatabaseErrors"/>. A <see cref="PostgresException"/> reaches the caller either
/// wrapped in a <see cref="DbUpdateException"/> (an immediate check during <c>SaveChanges</c>) or bare (a deferred
/// check raised by the commit, parity report #25). The SQL Server shapes need a real <c>SqlException</c>, which has no
/// public constructor; the integration tests cover them.
/// </summary>
[TestClass]
public sealed class DatabaseErrorsTests
{
    private const string LockForeignKey = "FK_ConversationLocks_NotebookConversations_ConversationId";

    [TestMethod]
    public void Foreign_key_violation_is_recognized_wrapped_in_a_DbUpdateException()
    {
        var wrapped = new DbUpdateException("save failed", ForeignKeyViolation(LockForeignKey));

        DatabaseErrors.IsForeignKeyViolation(wrapped, LockForeignKey).Should().BeTrue();
    }

    [TestMethod]
    public void Foreign_key_violation_is_recognized_as_a_top_level_PostgresException()
    {
        DatabaseErrors.IsForeignKeyViolation(ForeignKeyViolation(LockForeignKey), LockForeignKey).Should().BeTrue();
    }

    [TestMethod]
    public void Foreign_key_violation_requires_the_exact_constraint_name()
    {
        var other = ForeignKeyViolation("FK_UserRoles_Users_AssignedByUserId");

        DatabaseErrors.IsForeignKeyViolation(other, LockForeignKey).Should().BeFalse();
        DatabaseErrors.IsForeignKeyViolation(new DbUpdateException("save failed", other), LockForeignKey).Should().BeFalse();
        DatabaseErrors.IsForeignKeyViolation(ForeignKeyViolation(LockForeignKey.ToLowerInvariant()), LockForeignKey).Should().BeFalse();
    }

    [TestMethod]
    public void Foreign_key_violation_requires_sqlstate_23503()
    {
        var uniqueViolation = new PostgresException(
            "duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation, constraintName: LockForeignKey);

        DatabaseErrors.IsForeignKeyViolation(uniqueViolation, LockForeignKey).Should().BeFalse();
        DatabaseErrors.IsForeignKeyViolation(new DbUpdateException("save failed", uniqueViolation), LockForeignKey).Should().BeFalse();
    }

    [TestMethod]
    public void Non_provider_exceptions_are_not_foreign_key_violations()
    {
        DatabaseErrors.IsForeignKeyViolation(new InvalidOperationException(LockForeignKey), LockForeignKey).Should().BeFalse();
        DatabaseErrors.IsForeignKeyViolation(new DbUpdateException(LockForeignKey), LockForeignKey).Should().BeFalse();
        DatabaseErrors.IsForeignKeyViolation(
            new DbUpdateException("save failed", new InvalidOperationException(LockForeignKey)), LockForeignKey).Should().BeFalse();
    }

    [TestMethod]
    public void Serialization_failure_is_recognized_wrapped_or_top_level()
    {
        var failure = new PostgresException(
            "could not serialize access", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);

        DatabaseErrors.IsSerializationFailure(failure).Should().BeTrue();
        DatabaseErrors.IsSerializationFailure(new DbUpdateException("save failed", failure)).Should().BeTrue();
        DatabaseErrors.IsSerializationFailure(
            new PostgresException("deadlock", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected)).Should().BeFalse();
        DatabaseErrors.IsSerializationFailure(ForeignKeyViolation(LockForeignKey)).Should().BeFalse();
        DatabaseErrors.IsSerializationFailure(new InvalidOperationException("40001")).Should().BeFalse();
    }

    [TestMethod]
    public void Provider_error_of_a_PostgresException_is_its_sqlstate_and_constraint()
    {
        DatabaseErrors.TryGetProviderError(ForeignKeyViolation(LockForeignKey), out var code, out var state).Should().BeTrue();
        code.Should().Be("23503");
        state.Should().Be(LockForeignKey);

        var noConstraint = new PostgresException("relation does not exist", "ERROR", "ERROR", PostgresErrorCodes.UndefinedTable);
        DatabaseErrors.TryGetProviderError(new DbUpdateException("save failed", noConstraint), out code, out state).Should().BeTrue();
        code.Should().Be("42P01");
        state.Should().BeEmpty();
    }

    [TestMethod]
    public void Provider_error_of_a_client_side_NpgsqlException_has_no_sqlstate()
    {
        DatabaseErrors.TryGetProviderError(new NpgsqlException("connection lost"), out var code, out var state).Should().BeTrue();
        code.Should().BeEmpty();
        state.Should().BeEmpty();
    }

    [TestMethod]
    public void Other_exceptions_have_no_provider_error()
    {
        DatabaseErrors.TryGetProviderError(new InvalidOperationException("boom"), out var code, out var state).Should().BeFalse();
        code.Should().BeEmpty();
        state.Should().BeEmpty();

        // Only a DbUpdateException is unwrapped, as EF Core's execution strategy does; other wrappers are not.
        DatabaseErrors.TryGetProviderError(
            new InvalidOperationException("wrapper", ForeignKeyViolation(LockForeignKey)), out _, out _).Should().BeFalse();
    }

    private static PostgresException ForeignKeyViolation(string constraint) => new(
        $"insert or update violates foreign key constraint \"{constraint}\"",
        "ERROR",
        "ERROR",
        PostgresErrorCodes.ForeignKeyViolation,
        constraintName: constraint);
}
