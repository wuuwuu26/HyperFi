using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using HyperFi.Audio;
using HyperFi.Controls;
using HyperFi.Library;
using HyperFi.Localization;
using HyperFi.Streaming;
using Windows.Media;

namespace HyperFi;

public partial class MainWindow : Window
{
    private async void LoadHyperFiServerNavigation()
    {
        var loadVersion = ++_hyperfiNavigationLoadVersion;
        _hyperfiPlaylistsByTag.Clear();
        foreach (var item in NavListBox.Items
                     .OfType<ListBoxItem>()
                     .Where(item => item.Tag is string tag &&
                                    (tag.StartsWith("HyperFiServer:", StringComparison.Ordinal) ||
                                     tag.StartsWith("HyperFiServerPlaylist:", StringComparison.Ordinal) ||
                                     tag.StartsWith("LibraryGroup:HyperFiServer", StringComparison.Ordinal)))
                     .ToList())
            NavListBox.Items.Remove(item);

        // HyperFi Server libraries are shown in the shared Artists, Albums, and
        // Tracks views. Keep the server playlist cache populated for existing
        // server-owned playlist actions, but do not render separate server
        // library branches in the sidebar.
        foreach (var server in _settings.HyperFiServers ?? [])
        {
            if (loadVersion != _hyperfiNavigationLoadVersion)
                return;
            try
            {
                var playlists = await _hyperfiClient.GetPlaylistsAsync(server);
                if (loadVersion != _hyperfiNavigationLoadVersion)
                    return;
                foreach (var playlist in playlists)
                {
                    var tag = $"HyperFiServerPlaylist:{server.Id}:{playlist.Id}";
                    _hyperfiPlaylistsByTag[tag] = playlist;
                }
            }
            catch { }
        }

        ApplySidebarNavigationSettings();
    }

    private int GetHyperFiServerInsertIndex()
    {
        var insertIndex = NavListBox.Items.IndexOf(FoldersNavItem);
        if (insertIndex < 0)
            return -1;

        for (var index = insertIndex + 1; index < NavListBox.Items.Count; index++)
        {
            if (NavListBox.Items[index] is ListBoxItem { Tag: string tag } &&
                (tag == "LibraryGroup:LocalPlaylists" ||
                 tag.StartsWith("Playlist:", StringComparison.Ordinal)))
            {
                insertIndex = index;
            }
        }

        return insertIndex + 1;
    }

    private StackPanel CreateSmartPlaylistSidebarContent(string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new AvaloniaPath
        {
            Width = 13,
            Height = 13,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Data = FindResource<Geometry>("IconSmartPlaylist"),
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x2A)),
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x45)),
            StrokeThickness = 0.7,
            StrokeJoin = PenLineJoin.Round
        });
        sp.Children.Add(CreateSidebarEntryText(text));
        return sp;
    }

    private int InsertHyperFiServerNavItem(int index, string serverId, string view, string title, bool isEnabled = true)
    {
        var icon = view switch
        {
            "Artists" => "IconArtist",
            "Albums" => "IconAlbum",
            "Tracks" => "IconTrack",
            "Folders" => "IconFolder",
            _ => "IconServer"
        };
        var content = CreateSidebarEntryContent(icon, title);
        content.Margin = new Thickness(16, 0, 0, 0);
        NavListBox.Items.Insert(index, new ListBoxItem
        {
            Content = content,
            Tag = $"HyperFiServer:{serverId}:{view}",
            IsEnabled = isEnabled,
            Theme = FindResource<ControlTheme>("NavItemTheme")
        });
        return index + 1;
    }

    // ------------------------------------------------------------------
    // HyperFi Server browsing
    // ------------------------------------------------------------------

    private async Task ShowHyperFiServerAsync(string tag)
    {
        if (!TryParseHyperFiServerTag(tag, out var serverId, out var view))
            return;
        _activeHyperFiServer = (_settings.HyperFiServers ?? [])
            .FirstOrDefault(s => string.Equals(s.Id, serverId, StringComparison.Ordinal));
        if (_activeHyperFiServer is null)
            return;

        _hyperfiNavigationStack.Clear();
        // Start each server visit with a clean facet slate; genres/formats from another
        // library would otherwise filter the remote Tracks view to nothing.
        _selectedTrackGenres.Clear();
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        _activeHyperFiView = view == "Root" ? "Artists" : view;
        _updatingViewMode = true;
        HyperFiArtistsViewRadioButton.IsChecked = _activeHyperFiView == "Artists";
        _updatingViewMode = false;
        ContentTitleTextBlock.Text = $"{_activeHyperFiServer.Name} · {GetHyperFiViewTitle(_activeHyperFiView)}";
        await LoadHyperFiViewAsync();
    }

    /// <summary>Adapts the typed server target parser to existing sidebar callers.</summary>
    /// <param name="tag">Potential remote sidebar tag.</param>
    /// <param name="serverId">Parsed server identity or empty when invalid.</param>
    /// <param name="view">Parsed view identity or empty when invalid.</param>
    /// <returns>Whether the tag identifies a server destination.</returns>
    private static bool TryParseHyperFiServerTag(string tag, out string serverId, out string view)
    {
        var parsed = ServerNavigationTarget.TryParse(tag, out var target);
        serverId = target?.ServerId ?? string.Empty;
        view = target?.View ?? string.Empty;
        return parsed;
    }

    private static string GetHyperFiFavoriteKey(string serverId, string entityType, long id)
        => $"{serverId}:{entityType}:{id}";

    internal static string GetServerSourceKey(string serverId) => $"server:{serverId}";

    private bool IsHyperFiFavorite(HyperFiServerSettings server, string entityType, long id)
        => ActiveUserProfile.HyperFiServerFavorites.Contains(GetHyperFiFavoriteKey(server.Id, entityType, id));

    /// <summary>Returns the server-side track IDs the client currently marks as favourites for a remote server.</summary>
    /// <param name="server">Remote server whose client-side track favourites are collected.</param>
    /// <returns>The favourite track IDs.</returns>
    private List<long> GetHyperFiFavoriteTrackIds(HyperFiServerSettings server)
    {
        var prefix = $"{server.Id}:Track:";
        var ids = new List<long>();
        foreach (var key in ActiveUserProfile.HyperFiServerFavorites)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                long.TryParse(key.AsSpan(prefix.Length), out var id))
                ids.Add(id);
        }
        return ids;
    }

    private string GetHyperFiViewTitle(string view) => view switch
    {
        "Artists" => LocalizationManager.Current.Artists,
        "Albums" => LocalizationManager.Current.Albums,
        "Tracks" => LocalizationManager.Current.Tracks,
        "Folders" => LocalizationManager.Current.FolderStructure,
        _ => LocalizationManager.Current.HyperFiServers
    };

    private async void HyperFiServerViewModeRadioButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (!IsVisible || _updatingViewMode ||
            sender is not RadioButton { IsChecked: true, Tag: string view } ||
            _activeHyperFiServer is null)
            return;

        _hyperfiNavigationStack.Clear();
        _activeHyperFiView = view;
        ContentTitleTextBlock.Text = _activeHyperFiServer.Name;
        BackButton.IsVisible = _navigationStack.Count > 0;
        await LoadHyperFiViewAsync();
    }

    private readonly NavigationLoadOwnership _remoteViewLoads = new();

    /// <summary>Abandons remote view publication before invoking cancellation callbacks.</summary>
    private void CancelRemoteViewLoad()
    {
        _remoteViewLoads.Invalidate();
        CancelAndDispose(ref _hyperfiViewCts);
    }

    /// <summary>Loads a remote view and publishes only its current navigation and catalog request.</summary>
    /// <param name="filterArtistId">Optional owning server artist scope.</param>
    /// <param name="filterAlbumId">Optional owning server album scope.</param>
    /// <returns>A task completing after publication or rejection of an abandoned request.</returns>
    private async Task LoadHyperFiViewAsync(long? filterArtistId = null, long? filterAlbumId = null)
    {
        if (!_navigationRestores.TryAdmitNavigation()) return;
        var restoreRequest = _navigationRestores.Context;
        if (_activeHyperFiServer is null)
            return;

        var server = _activeHyperFiServer;
        var tag = _currentTopLevelTag;
        var generation = _unifiedLibraryLoads.Generation;
        var previousCancellation = _hyperfiViewCts;
        _hyperfiViewCts = new CancellationTokenSource();
        var ct = _hyperfiViewCts.Token;
        var request = _remoteViewLoads.Begin(ct);
        CancelAndDispose(ref previousCancellation);
        bool CanPublish() => _remoteViewLoads.IsCurrent(request) &&
            (restoreRequest is null || _navigationRestores.IsCurrent(restoreRequest)) &&
            generation == _unifiedLibraryLoads.Generation &&
            ReferenceEquals(_activeHyperFiServer, server) &&
            string.Equals(_currentTopLevelTag, tag, StringComparison.Ordinal);
        if (!CanPublish()) return;
        ShowContentLoadingSkeleton();
        using var loading = new ContentLoadingLease(HideContentLoadingSkeleton);
        using var loadingCancellation = ct.Register(() =>
        {
            if (Dispatcher.UIThread.CheckAccess()) loading.Dispose();
            else Dispatcher.UIThread.Post(loading.Dispose);
        });
        if (!CanPublish()) return;
        var provider = CreateHyperFiCatalogProvider(server);
        var view = filterAlbumId.HasValue ? "AlbumTracks"
            : filterArtistId.HasValue ? "ArtistAlbums"
            : _activeHyperFiView;

        ContentDataGrid.IsVisible = true;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        // Hide the other full-area views so navigating into a remote view from the
        // dashboard, radio, podcast, lyrics, or artist-info view (e.g. a dashboard
        // recent-album card) does not leave that view covering the remote content.
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        StatusTextBlock.Text = LocalizationManager.Current.HyperFiLoading;
        ContentDataGrid.ItemsSource = null;
        if (view != "AlbumTracks")
            HideAlbumDetailHeader();
        UpdateEntityFavoritesFilterToggle(_currentTopLevelTag);
        TrackFilterButton.IsVisible = view == "Tracks";
        SaveSmartPlaylistButton.IsVisible = view == "Tracks";
        if (view == "Tracks")
            UpdateSaveSmartPlaylistButtonState();
        _hyperfiTrackFacets = null;

        try
        {
            switch (view)
            {
                case "Artists":
                {
                    ApplyColumns("Artists");
                    var artists = await provider.GetArtistsAsync(ct);
                    if (!CanPublish()) return;
                    var rows = artists.Select(artist => ToCatalogArtistContentRow(artist, server))
                    .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                    .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showArtistArtworkView);
                    ContentDataGrid.IsVisible = !_showArtistArtworkView;
                    AlbumArtworkListBox.IsVisible = false;
                    ArtistArtworkListBox.IsVisible = _showArtistArtworkView;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Artists", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "ArtistAlbums":
                {
                    ApplyColumns("Albums");
                    var albums = await provider.GetAlbumsByArtistAsync(
                        filterArtistId!.Value,
                        includeArtwork: true,
                        cancellationToken: ct);
                    if (!CanPublish()) return;
                    var rows = MergeLogicalAlbumRows(albums.Select(album => ToCatalogAlbumContentRow(album, server)))
                        .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                        .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showAlbumArtworkView);
                    ContentDataGrid.IsVisible = !_showAlbumArtworkView;
                    AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
                    ArtistArtworkListBox.IsVisible = false;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Albums", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "Albums":
                {
                    ApplyColumns("Albums");
                    var albums = await provider.GetAlbumsAsync(includeArtwork: true, cancellationToken: ct);
                    if (!CanPublish()) return;
                    var rows = MergeLogicalAlbumRows(albums.Select(album => ToCatalogAlbumContentRow(album, server)))
                        .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                        .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showAlbumArtworkView);
                    ContentDataGrid.IsVisible = !_showAlbumArtworkView;
                    AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
                    ArtistArtworkListBox.IsVisible = false;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Albums", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "AlbumTracks":
                case "Tracks":
                {
                    AlbumViewModeBorder.IsVisible = false;
                    IReadOnlyList<LibraryCatalogTrack> catalogTracks;
                    if (filterAlbumId.HasValue)
                    {
                        catalogTracks = await provider.GetTracksByAlbumAsync(filterAlbumId.Value, filterArtistId, ct);
                    }
                    else
                    {
                        var loadedFacets = await _hyperfiClient.GetTrackFacetsAsync(server, ct);
                        if (!CanPublish()) return;
                        _hyperfiTrackFacets = loadedFacets
                            .Select(f => f with
                            {
                                IsFavorite = IsHyperFiFavorite(server, "Track", f.Id),
                                SourceKey = GetServerSourceKey(server.Id)
                            })
                            .ToList();
                        if (!CanPublish()) return;
                        catalogTracks = await ResolveHyperFiTrackRowsAsync(server, provider, ct);
                    }
                    if (!CanPublish()) return;
                    // Build the columns immediately before binding, mirroring the local
                    // Tracks view. Applying them before the long-running async load lets
                    // the DataGrid run layout passes while still empty; Avalonia then
                    // fails to realize the rows that are bound in the async continuation,
                    // leaving only the column headers visible (most noticeable with the
                    // large unfiltered track set, while the small favourites set still
                    // happened to render).
                    ApplyColumns("Tracks");
                    var rows = catalogTracks.Select(track => ToCatalogTrackContentRow(track, server)).ToList();
                    ContentDataGrid.ItemsSource = rows;
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
                    break;
                }

                case "Folders":
                {
                    ContentDataGrid.IsVisible = false;
                    FolderTreeView.IsVisible = true;
                    ShowHyperFiFolderLoadingState();
                    var tracks = await LoadHyperFiFolderTracksAsync(server, ct);
                    if (!CanPublish()) return;
                    var metadata = await LoadHyperFiFolderTrackMetadataAsync(server, tracks, ct);
                    if (!CanPublish()) return;
                    BuildHyperFiFolderTree(server, tracks, metadata);
                    ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(tracks.Count);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (CanPublish()) StatusTextBlock.Text = LocalizationManager.Current.HyperFiConnectionFailed;
            return;
        }
        finally
        {
            loading.Dispose();
            if (CanPublish()) FadeInVisibleContentSurface(CanPublish);
        }

        if (CanPublish()) StatusTextBlock.Text = string.Empty;
    }

    private HyperFiServerLibraryCatalogProvider CreateHyperFiCatalogProvider(HyperFiServerSettings server)
        => new(server, _hyperfiClient, (entityType, id) => IsHyperFiFavorite(server, entityType, id));

    /// <summary>Creates a catalog provider that never treats request failures as an empty library.</summary>
    /// <param name="server">Owning remote source.</param>
    /// <returns>A provider whose full catalog requests propagate failures.</returns>
    private HyperFiServerLibraryCatalogProvider CreateCompleteHyperFiCatalogProvider(HyperFiServerSettings server)
        => new(server, _hyperfiClient, (entityType, id) => IsHyperFiFavorite(server, entityType, id), requireComplete: true);

    private HyperFiServerNowPlayingMetadataProvider CreateHyperFiNowPlayingProvider(HyperFiServerSettings server)
        => new(server, _hyperfiClient);

    /// <summary>Loads a complete remote track catalog, reusing only a current validated disk snapshot.</summary>
    /// <param name="server">Owning source.</param>
    /// <param name="cancellationToken">Source cancellation.</param>
    /// <param name="favorites">Optional captured personal favorites for an isolated operation.</param>
    /// <returns>All tracks after every page succeeds, with current client favorites.</returns>
    private async Task<List<LibraryCatalogTrack>> LoadAllHyperFiTracksAsync(
        HyperFiServerSettings server,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? favorites = null)
    {
        // Every writer of the shared disk cache must use strict requests.
        var provider = favorites is null ? CreateCompleteHyperFiCatalogProvider(server)
            : new HyperFiServerLibraryCatalogProvider(server, _hyperfiClient,
                (entity, id) => favorites.Contains(GetHyperFiFavoriteKey(server.Id, entity, id)), requireComplete: true);
        // The server reports when its library index last changed; reuse the
        // locally cached track list while that timestamp is unchanged so the
        // full list does not have to be downloaded on every visit.
        var scanStatus = await _hyperfiClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogTrack>? tracks = null;
        if (libraryChangedAt.HasValue)
        {
            // Reading and deserializing the (potentially large) cache file runs on
            // a background thread so the UI thread is never blocked on disk I/O.
            tracks = await Task.Run(
                () => TryLoadHyperFiTrackListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (tracks is null)
        {
            // Match the server's maximum page size. Requesting more makes the
            // capped first page look like the final page and truncates the cache.
            const int pageSize = 5000;
            tracks = await LibraryCatalogPaging.LoadAllAsync(
                (page, token) => provider.GetTracksAsync(page, pageSize, token), pageSize, cancellationToken);

            if (libraryChangedAt.HasValue)
            {
                var toCache = tracks;
                await Task.Run(
                    () => SaveHyperFiTrackListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        // Favourites are stored client-side and can change without the server's
        // library timestamp changing, so re-apply them after loading (including
        // from the cache) instead of trusting the cached favourite flags.
        return tracks
            .Select(track => track with { IsFavorite = favorites is null
                ? IsHyperFiFavorite(server, "Track", track.Id)
                : favorites.Contains(GetHyperFiFavoriteKey(server.Id, "Track", track.Id)) })
            .ToList();
    }

    /// <summary>Reads only current-schema, version-matched complete track snapshots.</summary>
    /// <param name="server">Owning source.</param>
    /// <param name="libraryChangedAt">Current server library version.</param>
    /// <param name="tracks">Resolved cached rows on success.</param>
    /// <returns>Whether a valid complete cache was found.</returns>
    private static bool TryLoadHyperFiTrackListCache(
        HyperFiServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogTrack> tracks)
    {
        tracks = [];
        try
        {
            var path = GetHyperFiTrackListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<HyperFiTrackListCache>(File.ReadAllText(path));
            if (cache?.Tracks is null || cache.SchemaVersion != 2 || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            tracks = cache.Tracks
                .Select(track => track with
                {
                    PlaybackPath = HyperFiServerClient.GetStreamUrl(server, track.Id)
                })
                .ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Saves a successfully completed catalog using credential-free playback references.</summary>
    /// <param name="server">Owning source.</param>
    /// <param name="libraryChangedAt">Validated server library version.</param>
    /// <param name="tracks">Complete track catalog.</param>
    private static void SaveHyperFiTrackListCache(
        HyperFiServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogTrack> tracks)
    {
        try
        {
            var path = GetHyperFiTrackListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new HyperFiTrackListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                tracks.Select(track => track with
                {
                    PlaybackPath = PlaylistReferences.BuildTrack(server.Id, track.Id)
                }).ToList(), SchemaVersion: 2);
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
            // Cache failures must never prevent browsing a remote server.
        }
    }

    private static string GetHyperFiTrackListCachePath(HyperFiServerSettings server)
        => RemoteServerCache.TrackListCachePath(server);

    /// <summary>Loads remote artists without caching failures as a valid empty catalog.</summary>
    /// <param name="server">Owning source.</param>
    /// <param name="cancellationToken">Source cancellation.</param>
    /// <param name="favorites">Optional copied favorites for an isolated catalog load.</param>
    /// <returns>Complete artists with captured favorites when supplied, otherwise current client favorites.</returns>
    private async Task<List<LibraryCatalogArtist>> LoadAllHyperFiArtistsAsync(
        HyperFiServerSettings server,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? favorites = null)
    {
        var provider = favorites is null ? CreateCompleteHyperFiCatalogProvider(server)
            : new HyperFiServerLibraryCatalogProvider(server, _hyperfiClient,
                (entity, id) => favorites.Contains(GetHyperFiFavoriteKey(server.Id, entity, id)), requireComplete: true);
        var scanStatus = await _hyperfiClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogArtist>? artists = null;
        if (libraryChangedAt.HasValue)
        {
            artists = await Task.Run(
                () => TryLoadHyperFiArtistListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (artists is null)
        {
            artists = (await provider.GetArtistsAsync(cancellationToken)).ToList();
            if (libraryChangedAt.HasValue)
            {
                var toCache = artists;
                await Task.Run(
                    () => SaveHyperFiArtistListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        return artists
            .Select(artist => artist with { IsFavorite = favorites is null
                ? IsHyperFiFavorite(server, "Artist", artist.Id)
                : favorites.Contains(GetHyperFiFavoriteKey(server.Id, "Artist", artist.Id)) })
            .ToList();
    }

    /// <summary>Loads remote albums without caching failures as a valid empty catalog.</summary>
    /// <param name="server">Owning source.</param>
    /// <param name="cancellationToken">Source cancellation.</param>
    /// <param name="favorites">Optional copied favorites for an isolated catalog load.</param>
    /// <returns>Complete albums with captured favorites when supplied, otherwise current client favorites.</returns>
    private async Task<List<LibraryCatalogAlbum>> LoadAllHyperFiAlbumsAsync(
        HyperFiServerSettings server,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? favorites = null)
    {
        var provider = favorites is null ? CreateCompleteHyperFiCatalogProvider(server)
            : new HyperFiServerLibraryCatalogProvider(server, _hyperfiClient,
                (entity, id) => favorites.Contains(GetHyperFiFavoriteKey(server.Id, entity, id)), requireComplete: true);
        var scanStatus = await _hyperfiClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogAlbum>? albums = null;
        if (libraryChangedAt.HasValue)
        {
            albums = await Task.Run(
                () => TryLoadHyperFiAlbumListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (albums is null)
        {
            albums = (await provider.GetAlbumsAsync(includeArtwork: true, cancellationToken)).ToList();
            if (libraryChangedAt.HasValue)
            {
                var toCache = albums;
                await Task.Run(
                    () => SaveHyperFiAlbumListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        return albums
            .Select(album => album with { IsFavorite = favorites is null
                ? IsHyperFiFavorite(server, "Album", album.Id)
                : favorites.Contains(GetHyperFiFavoriteKey(server.Id, "Album", album.Id)) })
            .ToList();
    }

    private static bool TryLoadHyperFiArtistListCache(
        HyperFiServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogArtist> artists)
    {
        artists = [];
        try
        {
            var path = GetHyperFiArtistListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<HyperFiArtistListCache>(File.ReadAllText(path));
            if (cache?.Artists is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            artists = cache.Artists;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadHyperFiAlbumListCache(
        HyperFiServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogAlbum> albums)
    {
        albums = [];
        try
        {
            var path = GetHyperFiAlbumListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<HyperFiAlbumListCache>(File.ReadAllText(path));
            if (cache?.Albums is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            albums = cache.Albums;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveHyperFiArtistListCache(
        HyperFiServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogArtist> artists)
    {
        try
        {
            var path = GetHyperFiArtistListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new HyperFiArtistListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                artists.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
        }
    }

    /// <summary>Deletes the cached remote artist list for a server after profile or image metadata changes.</summary>
    /// <param name="server">Server whose artist list cache should be removed.</param>
    internal static void DeleteHyperFiArtistListCache(HyperFiServerSettings server)
    {
        try
        {
            var path = GetHyperFiArtistListCachePath(server);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void SaveHyperFiAlbumListCache(
        HyperFiServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogAlbum> albums)
    {
        try
        {
            var path = GetHyperFiAlbumListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new HyperFiAlbumListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                albums.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
        }
    }

    /// <summary>Builds a versioned artist cache filename excluding credentials and legacy partial catalogs.</summary>
    /// <param name="server">Owning source.</param>
    /// <returns>The current-schema cache path.</returns>
    private static string GetHyperFiArtistListCachePath(HyperFiServerSettings server)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{server.Id}|{server.BaseUrl}|complete-artists-v2")));
        return AppPaths.GetDataPath("remote-artist-cache", $"{key}.json");
    }

    /// <summary>Builds a versioned album cache filename excluding credentials and legacy partial catalogs.</summary>
    /// <param name="server">Owning source.</param>
    /// <returns>The current-schema cache path.</returns>
    private static string GetHyperFiAlbumListCachePath(HyperFiServerSettings server)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{server.Id}|{server.BaseUrl}|complete-albums-v2")));
        return AppPaths.GetDataPath("remote-album-cache", $"{key}.json");
    }

    /// <summary>
    /// Resolves the remote Tracks row set: facet-filtered when filters are active,
    /// otherwise the server search results, otherwise all tracks. Uses the same
    /// <see cref="MatchesTrackFilters"/> logic as the local Tracks view.
    /// </summary>
    /// <param name="server">Owning remote source.</param>
    /// <param name="provider">Active remote catalog provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The track rows to display.</returns>
    private async Task<IReadOnlyList<LibraryCatalogTrack>> ResolveHyperFiTrackRowsAsync(
        HyperFiServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        if (HasActiveFilters && _hyperfiTrackFacets is { } facets)
        {
            var ids = facets.Where(f => MatchesTrackFilters(f)).Select(f => f.Id).ToList();
            return await provider.GetTracksByIdsAsync(ids, cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
            return await provider.SearchTracksAsync(SearchTextBox.Text.Trim(), 500, cancellationToken);
        // Preserve this legacy view's tolerant fallback without allowing its
        // failed requests to write partial full-catalog disk snapshots.
        var loaded = await LibrarySourceLoader.LoadAsync<LibraryCatalogTrack>(GetServerSourceKey(server.Id),
            async token => await LoadAllHyperFiTracksAsync(server, token), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return loaded.Rows;
    }

    /// <summary>Re-resolves and rebinds remote Tracks rows after a facet filter change.</summary>
    private async Task ApplyHyperFiTrackFiltersAsync()
    {
        if (_activeHyperFiServer is null)
            return;
        var provider = CreateHyperFiCatalogProvider(_activeHyperFiServer);
        var catalogTracks = await ResolveHyperFiTrackRowsAsync(_activeHyperFiServer, provider, CancellationToken.None);
        var rows = catalogTracks.Select(track => ToCatalogTrackContentRow(track, _activeHyperFiServer)).ToList();
        BindRemoteTrackRows(rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
        UpdateSaveSmartPlaylistButtonState();
    }

    private void BindRemoteTrackRows(IReadOnlyList<ContentRow> rows)
    {
        ContentDataGrid.ItemsSource = rows;
    }

    private static async Task LoadHyperFiArtworkAsync(ContentRow row, string artUrl)
    {
        try
        {
            var artwork = await LoadRemoteArtworkImageAsync(artUrl, 320);
            var thumbnail = await LoadRemoteArtworkImageAsync(artUrl, 96);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                row.Artwork = artwork;
                row.Thumbnail = thumbnail;
                row.ArtworkLoadQueued = false;
                row.ArtworkLoadCompleted = true;
                row.ThumbnailLoadQueued = false;
                row.ThumbnailLoadCompleted = true;
            }, DispatcherPriority.Background);
        }
        catch { }
    }
}



