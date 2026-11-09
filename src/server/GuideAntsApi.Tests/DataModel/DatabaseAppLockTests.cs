using FluentAssertions;
using GuideAntsApi.Database;
using GuideAntsApi.Endpoints;
using GuideAntsApi.Services.Auth;

namespace GuideAntsApi.Tests.DataModel;

/// <summary>
/// On SQL Server the lock must send exactly the statements the callers sent before <see cref="DatabaseAppLock"/>
/// existed. The Postgres behavior (polling, timeout) runs against a real server in <c>QueueAndLockBehaviorTests</c>.
/// </summary>
[TestClass]
public sealed class DatabaseAppLockTests
{
    [TestMethod]
    public void Registration_lock_sends_the_original_sp_getapplock_statement()
    {
        DatabaseAppLock.SqlServerLockSql(AuthEndpoints.RegisterLockResource, AuthEndpoints.RegisterLockTimeout)
            .Should().Be("EXEC sp_getapplock @Resource = N'GuideAnts.Auth.Register', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;");
    }

    [TestMethod]
    public void Admin_mutation_lock_sends_the_original_sp_getapplock_statement()
    {
        DatabaseAppLock.SqlServerLockSql(AdminUserService.AdminMutationLockResource, AdminUserService.AdminMutationLockTimeout)
            .Should().Be("EXEC sp_getapplock @Resource = N'GuideAnts.Auth.AdminUsers', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;");
    }

    [TestMethod]
    public void SqlServer_statement_escapes_quotes_in_the_resource_name()
    {
        DatabaseAppLock.SqlServerLockSql("It's.Lock", TimeSpan.FromMilliseconds(250))
            .Should().Be("EXEC sp_getapplock @Resource = N'It''s.Lock', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 250;");
    }

    [TestMethod]
    public void Postgres_statement_tries_a_transaction_level_advisory_lock_on_the_hashed_name()
    {
        DatabaseAppLock.PostgresTryLockSql
            .Should().Be("SELECT pg_try_advisory_xact_lock(hashtextextended({0}, 0)) AS \"Value\"");
        DatabaseAppLock.PostgresPollInterval.Should().Be(TimeSpan.FromMilliseconds(100));
    }
}
