using SCI_PH.Mobile.Common.Authentication;

namespace SCI_PH.Mobile.Tests.Auth;

public class ClinicalSessionAndStorageTests
{
    private static readonly ClinicalUser Paramedic =
        new(Guid.NewGuid(), "paramedic", "paramedic@sci-ph.test", ["Paramedic"], [ClinicalPermissions.EmergenciesOperate]);

    [Fact]
    public void TheAccessTokenIsKeptInMemoryUntilTheUserIsVerified()
    {
        var session = new ClinicalSessionService();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);

        session.SetAuthentication("access-token", expiresAt);

        Assert.Equal("access-token", session.AccessToken);
        Assert.Equal(expiresAt, session.AccessTokenExpiresAtUtc);
        Assert.False(session.IsAuthenticated);
        Assert.False(session.CanOperateEmergencies);

        session.SetCurrentUser(Paramedic);

        Assert.True(session.IsAuthenticated);
        Assert.True(session.CanOperateEmergencies);
    }

    [Fact]
    public void ClearRemovesTokenExpiryAndUser()
    {
        var session = new ClinicalSessionService();
        var changes = 0;
        session.SessionChanged += (_, _) => changes++;
        session.SetAuthentication("access-token", DateTimeOffset.UtcNow.AddMinutes(15));
        session.SetCurrentUser(Paramedic);

        session.Clear();

        Assert.Null(session.AccessToken);
        Assert.Null(session.AccessTokenExpiresAtUtc);
        Assert.Null(session.CurrentUser);
        Assert.False(session.IsAuthenticated);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void ThePermissionDecidesNotTheRole()
    {
        var withPermission = new ClinicalUser(Guid.NewGuid(), "nurse", null, ["Nurse"], [ClinicalPermissions.EmergenciesOperate]);
        var withoutPermission = new ClinicalUser(Guid.NewGuid(), "paramedic", null, ["Paramedic"], ["VitalSigns.Write"]);

        Assert.True(withPermission.CanOperateEmergencies);
        Assert.False(withoutPermission.CanOperateEmergencies);
        Assert.False(new ClinicalUser(Guid.NewGuid(), "x", null, [], ["emergencies.operate"]).CanOperateEmergencies);
    }

    [Fact]
    public async Task TheRefreshTokenIsSavedReadReplacedAndCleared()
    {
        var storage = new FakeSecureStorage();
        var store = new MauiSecureTokenStore(storage);

        Assert.Null(await store.GetRefreshTokenAsync());

        await store.SaveRefreshTokenAsync("refresh-1");
        Assert.Equal("refresh-1", await store.GetRefreshTokenAsync());

        await store.SaveRefreshTokenAsync("refresh-2");
        Assert.Equal("refresh-2", await store.GetRefreshTokenAsync());
        Assert.Single(storage.Values);

        await store.ClearAsync();
        Assert.Null(await store.GetRefreshTokenAsync());
        Assert.Empty(storage.Values);
    }

    [Fact]
    public async Task AnUnreadableStoredTokenIsDiscarded()
    {
        var storage = new FakeSecureStorage();
        var store = new MauiSecureTokenStore(storage);
        await store.SaveRefreshTokenAsync("refresh-1");
        storage.FailReads = true;

        Assert.Null(await store.GetRefreshTokenAsync());
        Assert.Empty(storage.Values);
    }

    [Fact]
    public async Task AnEmptyRefreshTokenIsNeverStored()
    {
        var store = new MauiSecureTokenStore(new FakeSecureStorage());

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveRefreshTokenAsync(" "));
    }
}
