using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.ViewModels;

public partial class MainWindowViewModel: ObservableObject
{
    
    private readonly ILogger _logger;

    
    public MainWindowViewModel(ILoggerFactory loggerFactory )
    {
        _logger = loggerFactory.CreateLogger("MainWindowViewModel");
    }
    
}