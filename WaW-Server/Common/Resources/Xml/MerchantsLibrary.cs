#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Xml;

public class MerchantsLibrary {
    private static readonly Logger _log = new(typeof(MerchantsLibrary));

    public static readonly Dictionary<string, MerchantDesc> Merchants = new();

    public static void Load(string dir) {
        dir = Path.Combine(AppContext.BaseDirectory, dir);

        if (!Directory.Exists(dir)) {
            _log.Error($"Merchants directory not found. '{dir}'");
            return;
        }

        _log.Debug($"Loading merchants from {dir}...");

        var files = Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories);

        foreach (var file in files) {
            _log.Debug($"Loading Merchant XML {file}...");
            MakeDictionaries(XElement.Parse(File.ReadAllText(file)));
        }

        _log.Info("Merchants loaded successfully.");
    }

    private static void MakeDictionaries(XElement root) {
        foreach (var xml in root.Elements()) {
            var region = xml.GetAttribute<string>("region");
            Merchants.TryAdd(region, new MerchantDesc(xml, region));
        }
    }
}