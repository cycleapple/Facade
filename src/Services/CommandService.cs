namespace Facade.Services;

public interface ICommandService : IHostedService;

#pragma warning disable CS9113
public class CommandService(ILogger _logger, ConfigWindow _configWindow, ICommandManager _commandManager, IPlotService _plotService, IDalamudPluginInterface _pluginInterface) : ICommandService
{
  private const string FacadeCommand = "/facade";

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _commandManager.AddHandler(FacadeCommand, new CommandInfo(OnCommand)
    {
      HelpMessage = $"輸入「{FacadeCommand} help」查看說明。"
    });

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _commandManager.RemoveHandler(FacadeCommand);

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  private void OnCommand(string command, string arguments)
  {
    _logger.Debug($"command::'{command}' arguments::'{arguments}'");

    string[] args = arguments.Split(" ", StringSplitOptions.RemoveEmptyEntries);
    if (args.Length == 0)
    {
      _configWindow.SettingsOpen = false;
      _configWindow.OverlayOpen = false;
      _configWindow.Toggle();
      return;
    }

    switch (args[0])
    {
#if SAVE_MODE
      case "save-corner":
        _plotService.SaveCorner();
        break;
      case "save-json":
        _plotService.SaveJson();
        break;
#endif
      case "version":
        _logger.Chat($"v{_pluginInterface.Manifest.AssemblyVersion}");
        break;
      case "help":
      case "?":
        _logger.Chat("可用指令：");
        _logger.Chat($"  {command} help - 顯示指令說明");
        _logger.Chat($"  {command} version - 顯示插件版本");
        _logger.Chat($"  {command} - 開啟或關閉設定視窗");
        break;
      default:
        _logger.Chat("無效的指令：");
        _logger.Chat($"  {command} {arguments}");
        goto case "help";
    }
  }
}
