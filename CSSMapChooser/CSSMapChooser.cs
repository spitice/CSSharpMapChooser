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

    private MapConfig mapConfig = default!;

    private ConVar? mp_timelimit = null;

    private static int TIMELEFT_NEVER_END = 9999;

    public int timeleft {
        get {
            if (GetGameRules() == null) {
                Logger.LogError("Failed to find the Game Rules entity!");
                return TIMELEFT_NEVER_END;
            }

            if (mp_timelimit == null) {
                mp_timelimit = ConVar.Find("mp_timelimit");
                if (mp_timelimit == null) {
                    Logger.LogWarning("Failed to find the mp_timelimit ConVar and try to find again.");
                    return TIMELEFT_NEVER_END;
                }
            }

            var timelimit = mp_timelimit.GetPrimitiveValue<float>();
            if (timelimit < 0.001f) {
                // if `mp_timelimit 0`, then we treat it as `mp_timelimit 60`
                timelimit = 60.0f;
            }
            return (int)((GetGameRules().GameStartTime + timelimit * 60.0f) - Server.CurrentTime);
        }
    }

    public int extendsCount {get; private set;} = 0;

    public void incrementExtendsCount() {
        extendsCount++;
    }

    private Timer? mapVoteTimer = null;

    public VoteManager? voteManager {get; private set;} = null;

    public void setVoteManager(VoteManager voteManager) {
        if(this.voteManager != null && this.voteManager?.voteProgress == VoteManager.VoteProgress.VOTE_INITIATING ||
            this.voteManager?.voteProgress == VoteManager.VoteProgress.VOTE_IN_PROGRESS ||
            this.voteManager?.voteProgress == VoteManager.VoteProgress.VOTE_NEXTMAP_CONFIRMED)
            throw new InvalidOperationException("VoteManager setter is should only be called when vote is pending.");

        this.voteManager = voteManager;
    }

    public readonly string CHAT_PREFIX = $" {ChatColors.Green}[CSSMC]{ChatColors.Default}";


    public RockTheVote rockTheVoteModule {get; private set;} = default!;
    public Nomination nominationModule {get; private set;} = default!;

    public override void Load(bool hotReload)
    {
        Logger.LogInformation("Plugin load started");

        Logger.LogInformation("Initializing the MapConfig instance");
        mapConfig = new MapConfig(this);

        Logger.LogInformation("Initializing the plugin settings instance");
        new PluginSettings(this);

        Logger.LogInformation("Initializing the Next map data");

        CreateMapVoteTimer();

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

        Logger.LogInformation("Initializing Nomination module");
        nominationModule = new Nomination(this, mapConfig);

        Logger.LogInformation("Initializing Nomination RTV module");
        rockTheVoteModule = new RockTheVote(this, mapConfig);

        Logger.LogInformation("Adding commands...");
        AddCommand("css_nextmap", "shows nextmap information", CommandNextMap);
        AddCommand("css_timeleft", "shows current map limit time", CommandTimeLeft);
        AddCommand("css_revote", "Re-vote the current vote.", CommandReVote);

        AddCommand("css_cancelvote", "Cancel the current vote", CommandCancelVote);

        AddCommandListener("say", ChatCommandTrigger, HookMode.Pre);
        AddCommandListener("say_team", ChatCommandTrigger, HookMode.Pre);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        mapConfig.ReloadConfigData();
    }

    public override void Unload(bool hotReload)
    {
        RemoveCommandListener("say", ChatCommandTrigger, HookMode.Pre);
        RemoveCommandListener("say_team", ChatCommandTrigger, HookMode.Pre);
    }

    private HookResult ChatCommandTrigger(CCSPlayerController? client, CommandInfo commandInfo) {
        if(client == null)
            return HookResult.Continue;

        string arg1 = commandInfo.GetArg(1);
        bool isTriggerContains = false;


        if(arg1.Equals("nextmap", StringComparison.OrdinalIgnoreCase)) {
            client.ExecuteClientCommandFromServer("css_nextmap");
            isTriggerContains = true;
        }
        else if (arg1.Equals("timeleft", StringComparison.OrdinalIgnoreCase)) {
            client.ExecuteClientCommandFromServer("css_timeleft");
            isTriggerContains = true;
        }
        else if (arg1.Equals("rtv", StringComparison.OrdinalIgnoreCase)) {
            client.ExecuteClientCommandFromServer("css_rtv");
            isTriggerContains = true;
        }

        if(isTriggerContains)
            return HookResult.Handled;

        return HookResult.Continue;
    }

    private void OnMapStart(string mapName) {
        CreateMapVoteTimer();
        voteManager = null;
        extendsCount = 0;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info) {
        if(voteManager != null && voteManager.shouldRestartAfterRoundEnd) {
            if(voteManager.nextMap == null) {
                Server.PrintToChatAll($"{CHAT_PREFIX} {ChatColors.DarkRed} Failed to transition to next map! See console to error log");
                Logger.LogError("Failed to transition to next map! Because next map is null! also this is should not be happened!");
                return HookResult.Continue;
            }
            ChangeToNextMap(voteManager.nextMap);
            return HookResult.Continue;
        }


        if(0 < timeleft || mp_timelimit?.GetPrimitiveValue<float>() < 0.0)
            return HookResult.Continue;

        float endMatchExtraTime = ConVar.Find("mp_competitive_endofmatch_extra_time")?.GetPrimitiveValue<float>() ?? 15.0F;

        AddTimer(endMatchExtraTime, () => {
            MapData? nextMap = voteManager?.nextMap;

            if(nextMap == null) {
                Random rand = new Random();
                List<MapData> mapData = mapConfig.GetMapDataList();
                ChangeToNextMap(mapData[rand.Next(mapData.Count())]);
            }
            else {
                ChangeToNextMap(nextMap);
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);

        return HookResult.Continue;
    }

    [RequiresPermissions(@"css/map")]
    private void CommandCancelVote(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;

        if(voteManager == null || voteManager.voteProgress != VoteManager.VoteProgress.VOTE_IN_PROGRESS) {

            client.PrintToChat($"{CHAT_PREFIX} There is no active vote!");
            return;
        }

        voteManager.CancelVote(client);
    }



    private void CommandReVote(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;

        if(voteManager == null || voteManager.voteProgress != VoteManager.VoteProgress.VOTE_IN_PROGRESS) {
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
            MapData? mapData = voteManager.nextMap;
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
            Server.PrintToChatAll($"{CHAT_PREFIX} {ChatColors.DarkRed}Failed to extending the map! See console to detailed information.");
            Logger.LogError($"Failed to extending the map! mp_timelimit ConVar not found!");
            return;
        }

        float oldTime = mp_timelimit.GetPrimitiveValue<float>();
        float newTime = oldTime + extendingTime;

        mp_timelimit.SetValue(newTime);
        Server.PrintToChatAll($"{CHAT_PREFIX} Timelimit extended by {extendingTime} minutes");
    }

    public int getHumanPlayersCount() {
        int totalHumanPlayers = 0;

        foreach(CCSPlayerController client in Utilities.GetPlayers()) {
            if(!client.IsValid || client.IsBot || client.IsHLTV)
                continue;

            totalHumanPlayers++;
        }

        return totalHumanPlayers;
    }


    private void CreateMapVoteTimer() {
        mapVoteTimer = AddTimer(1.0F, () => {
            if((int)PluginSettings.GetInstance().cssmcMapVoteStartTime.Value < timeleft)
                return;

            if(voteManager != null) {
                switch(voteManager.voteProgress) {
                    case VoteManager.VoteProgress.VOTE_FINISHED: {
                        return;
                    }
                    case VoteManager.VoteProgress.VOTE_IN_PROGRESS: {
                        return;
                    }
                    case VoteManager.VoteProgress.VOTE_INITIATING: {
                        return;
                    }
                    case VoteManager.VoteProgress.VOTE_NEXTMAP_CONFIRMED: {
                        return;
                    }
                    case VoteManager.VoteProgress.VOTE_PENDING: {
                        break;
                    }
                }
            }

            SimpleLogging.LogDebug("Creating a new VoteManager by Timer...");
            SimpleLogging.LogDebug($"  - mp_timelimit = {mp_timelimit?.GetPrimitiveValue<float>()}");
            SimpleLogging.LogDebug($"  - timeleft = {timeleft}");
            SimpleLogging.LogDebug($"  - VoteManager is {(voteManager == null ? "null" : "NOT null")}");

            voteManager = new VoteManager(mapConfig.GetMapDataList(), this, false);

            voteManager.StartVoteProcess();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
}