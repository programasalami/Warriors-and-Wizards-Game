using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Xml.Linq;
using Common.News;

namespace AccountServer.Systems.News;

// The News Board in the Nexus (2026-09-21): the patch notes file, newest first. Open to every client, nothing to sign in for.
//   <News><Entry date="2026-09-21" title="...">line&#10;line...</Entry>...</News>
public class NewsBoardList : RequestHandler {
    public override string Path => "/news/board";

    public override Task<string> Handle(string ip, NameValueCollection query) {
        var root = new XElement("News");
        foreach (var note in PatchNotes.Load()) {
            var entry = new XElement("Entry", new XAttribute("date", note.Date), new XAttribute("title", note.Title), string.Join("\n", note.Lines));
            if (!string.IsNullOrEmpty(note.Version))
                entry.SetAttributeValue("version", note.Version);
            root.Add(entry);
        }

        return Task.FromResult(root.ToString(SaveOptions.DisableFormatting));
    }
}
