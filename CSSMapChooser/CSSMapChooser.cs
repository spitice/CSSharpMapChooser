using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace CSSMapChooser;

public partial class CSSMapChooser : BasePlugin
{
    public override string ModuleName => "CounterStrikeSharp Map Chooser";

    public override string ModuleVersion => "0.0.1";

    public override string ModuleAuthor => "faketuna";

    public override string ModuleDescription => "CounterStrikeSharp implementation of map chooser";

    private MapConfig mapConfig = default!;

    private ConVar? mp_timelimit = null;

    private int timeleft = 0;

    public readonly string CHAT_PREFIX = $" {ChatColors.Green}[CSSMC]{ChatColors.Default}";

    public override void Load(bool hotReload)
    {
        Logger.LogInformation("Plugin load started");

        Logger.LogInformation("Initializing the MapConfig instance");
        mapConfig = new MapConfig(this);

        Logger.LogInformation("Initializing the Next map data");
        nextMapData = new MapData("NONE", false);

        Logger.LogInformation("Registering timeleft calculation timer.");
        RegisterListener<Listeners.OnTick>(() => {
            if (GetGameRules() != null && mp_timelimit != null)
				timeleft = (int)((GetGameRules().GameStartTime + mp_timelimit.GetPrimitiveValue<float>() * 60.0f) - Server.CurrentTime);
			else if (mp_timelimit == null) {
				Logger.LogWarning("Failed to find the mp_timelimit ConVar and try to find again.");
				mp_timelimit = ConVar.Find("mp_timelimit");
			}
			else {
				Logger.LogError("Failed to find the Game Rules entity!");
			}
        });

        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);

        Logger.LogInformation("Adding commands...");
        AddCommand("css_nextmap", "shows nextmap information", CommandNextMap);
        AddCommand("css_timeleft", "shows current map limit time", CommandTimeLeft);
        AddCommand("css_nominate", "nominate the specified map", CommandNominate);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        mapConfig.ReloadConfigData();
    }

    public override void Unload(bool hotReload)
    {
        
    }


    private void OnMapEnd() {
        initializeNominations();
    }

    private void CommandNextMap(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;
        
        ShowNextMapInfo(client);
    }

    private void ShowNextMapInfo(CCSPlayerController client) {
        client.PrintToChat($"{CHAT_PREFIX} Next map: {nextMapData.MapName}");
    }



    private void CommandTimeLeft(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;
        
        ShowTimeLeft(client);
    }

    private void ShowTimeLeft(CCSPlayerController client) {
        if(timeleft < 1) {
            client.PrintToChat($"{CHAT_PREFIX} Last round!");
        }
        else {
            client.PrintToChat($"{CHAT_PREFIX} Timeleft: {GetFormattedTimeLeft(timeleft)}");
        }
    }

    private string GetFormattedTimeLeft(int timeleft) {
        int minutes = timeleft / 60;
        int seconds = timeleft % 60;
        string formatted = "";

        if(minutes > 0) {
            formatted = $"{minutes} minutes {seconds} seconds";
        }
        else {
            formatted = $"{seconds} seconds";
        }

        return formatted;
    }

    private CCSGameRules GetGameRules()
	{
		return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
	}
}