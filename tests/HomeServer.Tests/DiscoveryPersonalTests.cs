using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class DiscoveryPersonalTests
{
    private static ProviderContent Movie(int id, int age = 0) => new("tmdb", id, "movie", "Isolated " + id, "Isolated " + id, "", "2020-01-01", null, null, "https://fixture.invalid/" + id, [], MinimumAge: age);
    [Fact]
    public void PersonalShelvesDoNotTreatAdministratorHouseholdVisibilityAsOwnership()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated"));
        var owner = home.Store.GetAccount("1")!;
        var member = home.Store.SaveAccount(null, new("Member", MaximumAge: 12, AllowUnrated: false), "isolated");
        var mine = home.Store.RequestContent(Movie(101), "all", "balanced", owner);
        var theirs = home.Store.RequestContent(Movie(102), "all", "balanced", member);
        var adult = home.Store.SaveContent(Movie(103, 18), owner);
        var shared = home.Store.SaveCollection(null, new("Isolated shared collection", true), owner);
        home.Store.ChangeCollectionItem(shared.Id, adult.Id, owner, false);
        Assert.Equal(new[] { mine.Id, adult.Id }.Order(), home.Store.GetDiscoveryPersonal("movie", "watchlist", 0, 30, owner).Items.Select(item => item.Id).Order());
        Assert.Equal(theirs.Id, Assert.Single(home.Store.GetDiscoveryPersonal("movie", "watchlist", 0, 30, member).Items).Id);
        Progress(home, owner.Id, mine.Id, 30, false);
        Progress(home, member.Id, theirs.Id, 120, true);
        Assert.Equal(mine.Id, Assert.Single(home.Store.GetDiscoveryPersonal("movie", "history", 0, 30, owner).Items).Id);
        Assert.Equal(theirs.Id, Assert.Single(home.Store.GetDiscoveryPersonal("movie", "history", 0, 30, member).Items).Id);
        Assert.Empty(home.Store.GetDiscoveryPersonal("movie", "continue", 0, 30, member).Items);
        Assert.Equal(mine.Id, Assert.Single(home.Store.GetDiscoveryPersonal("movie", "continue", 0, 30, owner).Items).Id);
        home.Store.SetContentPresentation(theirs.Id, new(Blocked: true));
        Assert.Empty(home.Store.GetDiscoveryPersonal("movie", "history", 0, 30, member).Items);
        Assert.Empty(home.Store.GetDownloads());
    }
    [Fact]
    public void PersonalPaginationIsBoundedAndBookmarkedContentDoesNotRequireAnAcquisition()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated")); var owner = home.Store.GetAccount("1")!;
        var first = home.Store.SaveContent(Movie(201), owner); var second = home.Store.SaveContent(Movie(202), owner);
        home.Store.SetPersonalContent(first.Id, owner, new(true, 8)); home.Store.SetPersonalContent(second.Id, owner, new(true));
        var page = home.Store.GetDiscoveryPersonal("movie", "watchlist", 0, 1, owner);
        Assert.Equal(2, page.Total); Assert.Single(page.Items); Assert.Equal(1, page.Limit);
        var next = home.Store.GetDiscoveryPersonal("movie", "watchlist", 1, 1, owner); Assert.Single(next.Items); Assert.NotEqual(page.Items[0].Id, next.Items[0].Id);
        Assert.Throws<DomainException>(() => home.Store.GetDiscoveryPersonal("movie", "history", -1, 1, owner));
        Assert.Throws<DomainException>(() => home.Store.GetDiscoveryPersonal("movie", "history", 0, 10000, owner));
        Assert.Throws<DomainException>(() => home.Store.GetDiscoveryPersonal("book", "history", 0, 30, owner));
        Assert.Empty(home.Store.GetDownloads());
    }
    private static void Progress(TestWorkspace home, string user, string content, double position, bool watched)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data, "home.db")}"); db.Open(); using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO content_watch_state(user_id,content_id,episode_key,position_seconds,watched,updated_at) VALUES($user,$content,'',$position,$watched,'2026-10-18T00:00:00+00:00')";
        command.Parameters.AddWithValue("$user", user); command.Parameters.AddWithValue("$content", content); command.Parameters.AddWithValue("$position", position); command.Parameters.AddWithValue("$watched", watched); command.ExecuteNonQuery();
    }
}
