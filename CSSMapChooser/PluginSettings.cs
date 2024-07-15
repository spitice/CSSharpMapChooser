using System.Runtime.CompilerServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using Microsoft.Extensions.Logging;

namespace CSSMapChooser {
    public class PluginSettings {

        private static PluginSettings? settingsInstance;

        public const string CONFIG_FOLDER = "cssmc/";
        private const string CONFIG_FILE = "config.cfg";

        public static PluginSettings GetInstance() {
            if(settingsInstance == null)
                throw new InvalidOperationException("Settings instance is not initialized yet.");

            return settingsInstance;
        }


        /*
        * Rock the Vote
        */
        public FakeConVar<float> cssmcRTVMapChangingDelay = new("cssmc_rtv_map_changing_delay", "When map changes after map vote with rtv.", 10.0F);
        public FakeConVar<bool> cssmcRTVMapChangingAfterRoundEnd = new("cssmc_rtv_map_changing_after_round_end", "Should change map after round end?", true);

        /*
        * Voting general
        */
        public FakeConVar<float> cssmcMapVoteCountdownTime = new("cssmc_map_vote_countdown_time", "How long to wait before vote starts after map vote notification?", 15.0F);


        private CSSMapChooser plugin;

        public PluginSettings(CSSMapChooser plugin) {
            SimpleLogging.LogDebug("Setting the instance info");
            settingsInstance = this;
            SimpleLogging.LogDebug("Setting the plugin instance");
            this.plugin = plugin;
            SimpleLogging.LogDebug("Initializing the settings");
            initializeSettings();
            SimpleLogging.LogDebug("Registering the fake convar");
            plugin.RegisterFakeConVars(typeof(PluginSettings), this);
        }

        public bool initializeSettings() {
            SimpleLogging.LogDebug("Generate path to config folder");
            string configFolder = Path.Combine(Server.GameDirectory, "csgo/cfg/", CONFIG_FOLDER);

            SimpleLogging.LogDebug("Checking existence of config folder");
            if(!Directory.Exists(configFolder)) {
                plugin.Logger.LogInformation($"Failed to find the config folder. Trying to generate...");

                Directory.CreateDirectory(configFolder);

                if(!Directory.Exists(configFolder)) {
                    plugin.Logger.LogError($"Failed to generate the Config folder!");
                    return false;
                }
            }

            SimpleLogging.LogDebug("Generate path to config file");
            string configLocation = Path.Combine(configFolder, CONFIG_FILE);

            SimpleLogging.LogDebug("Checking existence of config file");
            if(!File.Exists(configLocation)) {
                plugin.Logger.LogInformation($"Failed to find the config file. Trying to generate...");

                try {
                    generateCFG(configLocation);
                } catch(Exception e) {
                    plugin.Logger.LogError($"Failed to generate config file!\n{e.StackTrace}");
                    return false;
                }
                
                plugin.Logger.LogInformation($"Config file created.");
            }

            SimpleLogging.LogDebug("Executing config");
            SimpleLogging.LogDebug($"exec {CONFIG_FOLDER}{CONFIG_FILE}");
            Server.ExecuteCommand($"exec {CONFIG_FOLDER}{CONFIG_FILE}");
            return true;
        }

        private void generateCFG(string configPath) {
            StreamWriter config = File.CreateText(configPath);

            writeConVarConfig(config, cssmcRTVMapChangingDelay);
            writeConVarConfig(config, cssmcRTVMapChangingAfterRoundEnd);
            config.WriteLine("\ns");

            writeConVarConfig(config, cssmcMapVoteCountdownTime);


            config.Close();
        }

        private static void writeConVarConfig<T>(StreamWriter configFile, FakeConVar<T> convar)where T : IComparable<T>{
            configFile.WriteLine($"// {convar.Description}");
            if(typeof(T) == typeof(bool)) {
                var conValue = convar.Value;
                bool value = Unsafe.As<T, bool>(ref conValue);
                configFile.WriteLine($"{convar.Name} {Convert.ToInt32(value)}");
            } else {
                configFile.WriteLine($"{convar.Name} {convar.Value}");
            }
            configFile.WriteLine();
        }
    }
}