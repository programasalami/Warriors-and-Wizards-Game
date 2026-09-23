#region

using System.Xml.Linq;
using Common.Utilities;
using Newtonsoft.Json;

#endregion

namespace Common.Resources.Config;

public class NewsConfig {
    private const string ConfigFile = "Resources/Config/Data/newsConfig.xml";

    public NewsConfig(XElement e) {
        Models = JsonConvert.DeserializeObject<NewsItemModel[]>(e.GetValue<string>("Models"));
    }

    public static NewsConfig Config
        => ConfigLoader<NewsConfig>.Load(ConfigFile, e => new NewsConfig(e));

    public NewsItemModel[] Models { get; private set; }
}

public class NewsItemModel {
    public string Icon { get; set; }
    public string Title { get; set; }
    public string TagLine { get; set; }
    public string Link { get; set; }
    public int Date { get; set; }

    public XElement ToXml() {
        return new XElement("NewsItem",
            new XElement("Icon", Icon),
            new XElement("Title", Title),
            new XElement("TagLine", TagLine),
            new XElement("Link", Link),
            new XElement("Date", Date)
        );
    }
}
