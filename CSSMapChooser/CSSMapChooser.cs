using CounterStrikeSharp.API.Core;

namespace CSSMapChooser;

public class CSSMapChooser : BasePlugin
{
    public override string ModuleName => "CounterStrikeSharp Map Chooser";

    public override string ModuleVersion => "0.0.1";

    public override string ModuleAuthor => "faketuna";

    public override string ModuleDescription => "CounterStrikeSharp implementation of map chooser";

    private PluginConfig pluginConfig;

    CSSMapChooser() {
        pluginConfig = new PluginConfig(this);
    }

    public override void Load(bool hotReload)
    {
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        pluginConfig.ReloadConfigData();
    }

    public override void Unload(bool hotReload)
    {
        
    }

}