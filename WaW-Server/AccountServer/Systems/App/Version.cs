#region

using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Xml.Linq;
using Common.Resources.Config;

#endregion

namespace AccountServer.Systems.App;

// Tells a client, before anyone signs in, which build the game server currently accepts - the same value the game server checks in
// Hello (GameServerConfig.Version), so this can never disagree with what would actually let the client in. A client whose own build
// differs shows an "update required" prompt instead of failing later at connect time.
public class Version : RequestHandler {
    public override string Path => "/app/version";

    public override Task<string> Handle(string ip, NameValueCollection query) {
        var version = new XElement("Version", GameServerConfig.Config.Version);
        var downloadUrl = AppEngineConfig.Config.DownloadUrl;
        if (!string.IsNullOrEmpty(downloadUrl))
            version.SetAttributeValue("downloadUrl", downloadUrl);
        return Task.FromResult(version.ToString(SaveOptions.DisableFormatting));
    }
}
