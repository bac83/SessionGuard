using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Server.Ui.Components.Pages;
using Server.Ui.Models;
using Server.Ui.Services;

namespace Server.Ui.Tests;

public sealed class AgentsPageTests : TestContext
{
    [Fact]
    public void AgentsPage_ShowsAgentVersion()
    {
        var store = Substitute.For<IAdminDashboardStore>();
        store.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(new DashboardSnapshot(
            [new ChildProfile("child-01", "Sara", 90, true, 15, 75, new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero))],
            [new AgentStatus("agent-01", "1.2.3", "mond", "sara", "child-01", true, false, 15, 75, new DateTimeOffset(2026, 4, 8, 12, 5, 0, TimeSpan.Zero))],
            new DateTimeOffset(2026, 4, 8, 12, 5, 0, TimeSpan.Zero)));

        Services.AddSingleton(store);

        var cut = RenderComponent<Agents>();

        Assert.Contains("1.2.3", cut.Markup);
    }
}
