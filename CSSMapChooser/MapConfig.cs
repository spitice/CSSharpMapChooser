using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace CSSMapChooser;

class MapConfig {
    
    private readonly CSSMapChooser plugin;

    private List<MapData> mapData = new List<MapData>();

    public MapConfig(CSSMapChooser plugin) {
        this.plugin = plugin;
    }

    public void ReloadConfigData() {
        SimpleLogging.LogDebug("Start loading the map data.");
        bool isMapDataLoadSuccess = LoadMapData();

        if(!isMapDataLoadSuccess) 
            throw new InvalidOperationException("Map data loading failed!");

        SimpleLogging.LogDebug("Map data has been loaded successfully.");
    }

    public List<MapData> GetMapDataList() {
        return mapData;
    }

    public MapData? GetMapData(string mapName) {
        foreach(var data in mapData) {
            if(data.MapName == mapName)
                return data;
        }
        return null;
    }

    private bool LoadMapData() {
        string mapsTxtLocation = Path.Combine(plugin.ModuleDirectory + "\\maps.txt");
        SimpleLogging.LogTrace($"maps.txt file location: {mapsTxtLocation}");

        if(!Path.Exists(mapsTxtLocation)) {
            plugin.Logger.LogError("Failed to find maps.txt! If you haven't created yet, please create maps.txt in the {moduleDir} directory.", plugin.ModuleDirectory);
            return false;
        }

        SimpleLogging.LogTrace("Start iterating the maps.txt");
        foreach(var lines in File.ReadLines(mapsTxtLocation)) {

            string rawMapName = Regex.Replace(lines, @"^ws:", "", RegexOptions.None);

            bool isWorkshop = false;

            if(lines.StartsWith("ws:")) {
                isWorkshop = true;
            }

            SimpleLogging.LogTrace($"map name: {rawMapName}, workshop?: {isWorkshop}");
            mapData.Add(new MapData(rawMapName, isWorkshop));
        }
        return true;
    }
}