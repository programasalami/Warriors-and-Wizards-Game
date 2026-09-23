using System;
using System.IO;
using System.Xml.Linq;

namespace Common.Resources.Config;

// Shared XML config loading/caching. Each config class only owns its own
// XML-to-object parsing (via the factory delegate); path resolution, file
// reading, existence checking, and caching all live here in one place.
public static class ConfigLoader<T> where T : class {
    private static T _instance;

    public static T Load(string relativePath, Func<XElement, T> factory) {
        if (_instance != null)
            return _instance;

        var configPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Config file not found: '{configPath}'", configPath);

        return _instance = factory(XElement.Parse(File.ReadAllText(configPath)));
    }
}
