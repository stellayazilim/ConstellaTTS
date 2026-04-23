using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExCSS;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.ViewModels;

public partial class MainWindowViewModel: ObservableObject
{
    
    private readonly ILogger _logger;
    
    [RelayCommand]
    public void PointerCommand(PointerEventArgs e)
    {
      //  _logger.LogInformation($"Playing {e.GetPosition(null).X}");
    }
    
    public MainWindowViewModel(ILoggerFactory loggerFactory )
    {
        _logger = loggerFactory.CreateLogger("MainWindowViewModel");
    }
    
}