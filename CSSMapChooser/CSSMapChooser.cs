using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CSSMapChooser;

public partial class CSSMapChooser : BasePlugin
{
    public override string ModuleName => "CounterStrikeSharp Map Chooser";

    public override string ModuleVersion => "0.0.1";

    public override string ModuleAuthor => "faketuna";

    public override string ModuleDescription => "CounterStrikeSharp implementation of map chooser";

    private Timer? mapVoteTimer = null;

    private MapConfig mapConfig = default!;

    private ConVar? mp_timelimit = null;

    private int timeleft = 0;

    private VoteManager? voteManager = null;

    public VoteManager? GetVoteManager() {
        return voteManager;
    }

    public void SetVoteManager(VoteManager voteManager) {
        if(this.voteManager?.GetNextMap() != null)
            return;
        
        this.voteManager = voteManager;
    }

    private Nomination nominationModule = default!;
    
    public Nomination GetNominationModule() {
        return nominationModule;
    }

    private RockTheVote rockTheVoteModule = default!;

    public RockTheVote GetRockTheVoteModule() {
        return rockTheVoteModule;
    }

    public readonly string CHAT_PREFIX = $" {ChatColors.Green}[CSSMC]{ChatColors.Default}";

    public override void Load(bool hotReload)
    {
        Logger.LogInformation("Plugin load started");

        Logger.LogInformation("Initializing the MapConfig instance");
        mapConfig = new MapConfig(this);

        Logger.LogInformation("Initializing the plugin settings instance");
        new PluginSettings(this);

        nominationModule = new Nomination(this, mapConfig);

        rockTheVoteModule = new RockTheVote(this, mapConfig);

        Logger.LogInformation("Initializing the Next map data");

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

        CreateMapVoteTimer();

        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);

        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

        Logger.LogInformation("Adding commands...");
        AddCommand("css_nextmap", "shows nextmap information", CommandNextMap);
        AddCommand("css_timeleft", "shows current map limit time", CommandTimeLeft);
        AddCommand("css_revote", "Re-vote the current vote.", CommandReVote);

        AddCommand("css_cancelvote", "Cancel the current vote", CommandCancelVote);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        mapConfig.ReloadConfigData();
    }

    public override void Unload(bool hotReload)
    {
        
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info) {

        if(timeleft < 0 && mp_timelimit?.GetPrimitiveValue<float>() > 0.0) {

            float endMatchExtraTime = ConVar.Find("mp_competitive_endofmatch_extra_time")?.GetPrimitiveValue<float>() ?? 15.0F;
            AddTimer(endMatchExtraTime, () => {
                MapData? nextMap = voteManager?.GetNextMap();

                if(nextMap == null) {
                    Random rand = new Random();
                    List<MapData> mapData = mapConfig.GetMapDataList();
                    ChangeToNextMap(mapData[rand.Next(mapData.Count())]);
                }
                else {
                    ChangeToNextMap(nextMap);
                }
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            return HookResult.Continue;
        }

        if(voteManager != null && voteManager.ShouldRestartAfterRoundEnd()) {
            ChangeToNextMap(voteManager.GetNextMap()!);
            return HookResult.Continue;
        }


        return HookResult.Continue;
    }

    private void OnMapStart(string mapName) {
        CreateMapVoteTimer();
    }

    private void OnMapEnd() {
        nominationModule.initializeNominations();
        voteManager = null;
        mapVoteTimer?.Kill();
    }

    [RequiresPermissions(@"css/map")]
    private void CommandCancelVote(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;

        if(voteManager == null || voteManager.GetVoteProgress() != VoteManager.VoteProgress.VOTE_IN_PROGRESS) {

            client.PrintToChat($"{CHAT_PREFIX} There is no active vote!");
            return;
        }

        voteManager.CancelVote(client);
    }



    private void CommandReVote(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;

        if(voteManager == null || voteManager.GetVoteProgress() != VoteManager.VoteProgress.VOTE_IN_PROGRESS) {

            client.PrintToChat($"{CHAT_PREFIX} There is no active vote!");
            return;
        }

        voteManager.ShowVotingMenu(client);
    }

    private void CommandNextMap(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;
        
        ShowNextMapInfo(client);
    }

    private void ShowNextMapInfo(CCSPlayerController client) {
        string nextMapInfo = "Pending vote";

        if (voteManager != null) {
            MapData? mapData = voteManager.GetNextMap();
            if (mapData != null) {
                nextMapInfo = mapData.MapName;
            }
        }

        client.PrintToChat($"{CHAT_PREFIX} Next map: {nextMapInfo}");
    }



    private void CommandTimeLeft(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;
        
        ShowTimeLeft(client);
    }

    private void ShowTimeLeft(CCSPlayerController client) {
        if(mp_timelimit?.GetPrimitiveValue<float>() <= 0.0) {
            client.PrintToChat($"{CHAT_PREFIX} No time limit");
        }
        else if(timeleft < 1) {
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

    public void ChangeToNextMap(MapData nextMap) {
        string serverCmd = "";

        if(nextMap.isWorkshopMap) {
            // TODO: get workshop id for executing host_workshop_map instead of ds_workshop_changelevel
            // serverCmd += $"host_workshop_map {}";
            
            serverCmd += $"ds_workshop_changelevel {nextMap.MapName}";
        }
        else {
            serverCmd += $"map {nextMap.MapName}";
        }

        SimpleLogging.LogDebug($"Changing map. cmd: {serverCmd}");
        Server.ExecuteCommand(serverCmd);
    }

    public void ExtendCurrentMap(float extendingTime) {
        if(mp_timelimit == null) {
            Server.PrintToChatAll($"{CHAT_PREFIX} Failed to extending the map! See console to detailed information.");
            Logger.LogError($"Failed to extending the map! mp_timelimit ConVar not found!");
            return;
        }

        float oldTime = mp_timelimit.GetPrimitiveValue<float>();
        float newTime = oldTime + extendingTime;

        mp_timelimit.SetValue(newTime);
        Server.PrintToChatAll($"{CHAT_PREFIX} Timelimit extended by {extendingTime} minutes");
    }

    private void CreateMapVoteTimer() {
        mapVoteTimer = AddTimer(1.0F, () => {
            if((int)PluginSettings.GetInstance().cssmcMapVoteStartTime.Value < timeleft)
                return;

            if(voteManager != null){
                if(voteManager.GetNextMap() != null)
                    return;
                
                if(voteManager.GetVoteProgress() == VoteManager.VoteProgress.VOTE_IN_PROGRESS ||
                    voteManager.GetVoteProgress() == VoteManager.VoteProgress.VOTE_STARTING
                )
                    return;
            }

            voteManager = new VoteManager(nominationModule, mapConfig.GetMapDataList(), this, false);

            voteManager.StartVoteProcess();
            
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
}