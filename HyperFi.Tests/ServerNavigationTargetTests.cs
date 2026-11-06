using Xunit;

namespace HyperFi.Tests;

/// <summary>Protects credential-free server routing and album source ownership without a window.</summary>
public sealed class ServerNavigationTargetTests
{
    /// <summary>Non-server and malformed tags cannot resolve against ambient server state.</summary>
    /// <param name="tag">Invalid navigation identity.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Tracks")]
    [InlineData("PlexLibrary:fixture:1")]
    [InlineData("HyperFiServerPlaylist:fixture:1")]
    [InlineData("hyperfiserver:fixture:Albums")]
    [InlineData("HyperFiServer:")]
    [InlineData("HyperFiServer: :Albums")]
    [InlineData("HyperFiServer::Albums")]
    public void InvalidTagsHaveNoTarget(string? tag)
    {
        Assert.False(ServerNavigationTarget.TryParse(tag, out var target));
        Assert.Null(target);
    }

    /// <summary>The parser preserves legacy Root defaults and existing view grammar.</summary>
    /// <param name="tag">Existing server tag.</param>
    /// <param name="view">Expected parsed view.</param>
    [Theory]
    [InlineData("HyperFiServer:fixture", "Root")]
    [InlineData("HyperFiServer:fixture:", "Root")]
    [InlineData("HyperFiServer:fixture: ", "Root")]
    [InlineData("HyperFiServer:fixture:Artists", "Artists")]
    [InlineData("HyperFiServer:fixture:Albums", "Albums")]
    [InlineData("HyperFiServer:fixture:Tracks", "Tracks")]
    [InlineData("HyperFiServer:fixture:Folders", "Folders")]
    [InlineData("HyperFiServer:fixture:Legacy:Detail", "Legacy:Detail")]
    public void ExistingTagsRetainTheirServerAndView(string tag, string view)
    {
        Assert.True(ServerNavigationTarget.TryParse(tag, out var target));
        Assert.Equal("fixture", target!.ServerId);
        Assert.Equal(view, target.View);
        Assert.Equal($"HyperFiServer:fixture:{view}", target.Tag);
    }

    /// <summary>Album capture cannot inherit a foreign server's tag, even for matching numeric IDs.</summary>
    /// <param name="origin">Sidebar origin unrelated to the owning source.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("Dashboard")]
    [InlineData("HyperFiServer:other:Albums")]
    [InlineData("HyperFiServer:FIXTURE:Albums")]
    [InlineData("HyperFiServer::Albums")]
    public void AlbumTargetRepairsForeignOrInvalidOrigin(string? origin)
    {
        Assert.Equal("HyperFiServer:fixture:Albums",
            ServerNavigationTarget.ForAlbum("fixture", origin));
        var capture = NavigationCaptureDecision.Select(new(false, false, origin, 42, null,
            null, false, true, "fixture", false));
        Assert.Equal(NavigationCaptureKind.ServerAlbum, capture.Kind);
        Assert.Equal("HyperFiServer:fixture:Albums", capture.NavigationTag);
    }

    /// <summary>An origin from the owning source keeps its existing view.</summary>
    [Fact]
    public void AlbumTargetRetainsMatchingOrigin()
    {
        Assert.Equal("HyperFiServer:fixture:Tracks",
            ServerNavigationTarget.ForAlbum("fixture", "HyperFiServer:fixture:Tracks"));
        Assert.Equal("HyperFiServer:fixture", ServerNavigationTarget.ForAlbum("fixture", "HyperFiServer:fixture"));
        Assert.Equal("HyperFiServer:fixture:", ServerNavigationTarget.ForAlbum("fixture", "HyperFiServer:fixture:"));
    }
}
