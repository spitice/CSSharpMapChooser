using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace CSSMapChooser;

public class CSSMapChooser : BasePlugin
{
    public override string ModuleName => "CounterStrikeSharp Map Chooser";

    public override string ModuleVersion => "0.0.1";

    public override string ModuleAuthor => "faketuna";

    public override string ModuleDescription => "CounterStrikeSharp implementation of map chooser";

    private MapConfig mapConfig = default!;

    private MapData nextMapData = default!;

    private readonly string CHAT_PREFIX = $" {ChatColors.Green}[CSSMC]{ChatColors.Default}";

    public override void Load(bool hotReload)
    {
        Logger.LogInformation("Plugin load started");

        Logger.LogInformation("Initializing the MapConfig instance");
        mapConfig = new MapConfig(this);

        Logger.LogInformation("Initializing the Next map data");
        nextMapData = new MapData("NONE", false);

        Logger.LogInformation("Adding commands...");
        AddCommand("css_nextmap", "shows nextmap information", CommandNextMap);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        mapConfig.ReloadConfigData();
    }

    public override void Unload(bool hotReload)
    {
        
    }


    private void CommandNextMap(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;
        
        ShowNextMapInfo(client);
    }

    private void ShowNextMapInfo(CCSPlayerController client) {
        client.PrintToChat($"{CHAT_PREFIX} Next map: {nextMapData.MapName}");
    }
}