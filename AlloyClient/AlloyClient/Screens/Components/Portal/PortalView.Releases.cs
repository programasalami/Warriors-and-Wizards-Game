using System.Collections.Generic;
using AlloyClient.AppEngine;
using AlloyClient.Data;
using Alloy.UiLib.Core;

namespace AlloyClient.Screens.Components.Portal;

// The release history (2026-09-22): every patch-notes entry the server ships, newest first, with the game version it came with. Reached from
// RELEASE HISTORY on the Portal's home page (the nav row is full). Same data as the website's /releases.html and the News Board in the Nexus.
public sealed partial class PortalView {
    private PortalReleases _releases;

    private PortalReleases Releases(out string error) {
        error = null;
        if (_releases != null) {
            return _releases;
        }

        const string key = "releases";
        if (_errors.TryGetValue(key, out error)) {
            return null;
        }

        Fetch(key, PortalRequests.GetReleases, r => _releases = r.Data);
        return null;
    }

    private void BuildReleasesPage() {
        var data = Releases(out var error);
        PageTitle("RELEASE HISTORY", data != null && !string.IsNullOrEmpty(data.Version) ? $"game version {data.Version} now" : null);
        _content.AddChild(RefreshLink(() => { _releases = null; _errors.Remove("releases"); Render(); }));

        if (data == null) {
            Notice(error ?? "Loading...", 0, error == null ? InkSoft : Bad);
            return;
        }

        if (data.Releases.Count == 0) {
            Notice("No releases written down yet.");
            return;
        }

        var top = ContentTop + 60;
        var perPage = System.Math.Max(1, (ContentBottom - 28 - top) / ListRowH);
        var (first, count) = Pager(data.Releases.Count, perPage);
        for (var i = 0; i < count; i++) {
            var index = first + i;
            var r = data.Releases[index];
            var row = BuildRowButton(ContentLeft, top + i * ListRowH, ContentW, ListRowH - 3, () => Go(Page.Release, index.ToString()), 0.05f);
            var cy = (ListRowH - 3) / 2;
            row.AddChild(Text(string.IsNullOrEmpty(r.Version) ? "" : "v" + r.Version, SmallSize, 8, cy, UiAnchor.MiddleLeft, color: Hover, outline: 1));
            row.AddChild(Text(r.Date, SmallSize, 110, cy, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
            row.AddChild(Text(r.Title, SmallSize + 1, 250, cy, UiAnchor.MiddleLeft, outline: 1, maxWidth: ContentW - 270));
            _content.AddChild(row);
        }
    }

    // One release: its lines, paged by the height they really take (a line can wrap onto two or three).
    private void BuildReleasePage(string arg) {
        var data = Releases(out var error);
        if (data == null || !int.TryParse(arg, out var index) || index < 0 || index >= data.Releases.Count) {
            PageTitle("RELEASE HISTORY");
            Notice(data == null ? error ?? "Loading..." : "That release is not in the list.");
            return;
        }

        var r = data.Releases[index];
        var stamp = string.IsNullOrEmpty(r.Version) ? r.Date : $"v{r.Version}   {r.Date}";
        PageTitle(r.Title, stamp);

        var top = ContentTop + 64;
        var room = ContentBottom - 36 - top;
        const int gap = 10;
        var width = ContentW - 40;

        var starts = new List<int> { 0 };
        var used = 0;
        for (var i = 0; i < r.Lines.Count; i++) {
            var h = Text("- " + r.Lines[i], SmallSize + 1, 0, 0, UiAnchor.LeftTop, maxWidth: width).Height + gap;
            if (used > 0 && used + h > room) {
                starts.Add(i);
                used = 0;
            }

            used += h;
        }

        var (page, _) = Pager(starts.Count, 1);
        var end = page + 1 < starts.Count ? starts[page + 1] : r.Lines.Count;
        var y = top;
        for (var i = starts[page]; i < end; i++) {
            var line = Text("- " + r.Lines[i], SmallSize + 1, ContentLeft + 16, y, UiAnchor.LeftTop, outline: 1, maxWidth: width);
            _content.AddChild(line);
            y += line.Height + gap;
        }
    }
}
