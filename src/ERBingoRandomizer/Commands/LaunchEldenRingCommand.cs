using ERBingoRandomizer.ViewModels;
using System.ComponentModel;
using System.Diagnostics;

namespace ERBingoRandomizer.Commands;

public class LaunchEldenRingCommand : CommandBase {
    private readonly MainWindowViewModel _mwViewModel;
    public LaunchEldenRingCommand(MainWindowViewModel mwViewModel) {
        _mwViewModel = mwViewModel;
        _mwViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public override bool CanExecute(object? parameter) {
        return _mwViewModel.FilesReady && !_mwViewModel.InProgress;
    }

    public override void Execute(object? parameter) {
        if (EldenRingIsOpen()) {
            _mwViewModel.DisplayMessage("Elden Ring is still open. Please close Elden Ring or wait for it to full exit.");
            return;
        }
        _mwViewModel.ListBoxDisplay.Clear();
        _mwViewModel.DisplayMessage("Launching Elden Ring via ModEngine 2");
        LaunchGame();
    }
    private static void LaunchGame() {

        Process me2 = new() {
            StartInfo = new ProcessStartInfo {
                FileName = "launchmod_bingo.bat",
                WorkingDirectory = Const.ME2Path,
                UseShellExecute = true,
                CreateNoWindow = true,
            },
        };

        me2.Start();
    }
    private static bool EldenRingIsOpen() {
        Process[] processes = Process.GetProcesses();
        foreach (Process process in processes) {
            if (process.ProcessName is not "eldenring") {
                continue;
            }
            // if (process.HasExited) {
            //     _mwViewModel.DisplayMessage("Elden Ring is closing!");
            // }
            return true;
        }
        return false;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(MainWindowViewModel.FilesReady)
            or nameof(MainWindowViewModel.InProgress)) {
            OnCanExecuteChanged();
        }
    }
}

public class LaunchEldenRingNormalCommand : CommandBase {
    private readonly MainWindowViewModel _mwViewModel;
    public LaunchEldenRingNormalCommand(MainWindowViewModel mwViewModel) {
        _mwViewModel = mwViewModel;
        _mwViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public override bool CanExecute(object? parameter) {
        return true;
    }

    public override void Execute(object? parameter) {
        if (EldenRingIsOpen()) {
            _mwViewModel.DisplayMessage("艾尔登法环还在运行中，请彻底关闭游戏后再次点击。");
            return;
        }
        _mwViewModel.ListBoxDisplay.Clear();
        _mwViewModel.DisplayMessage("正在通过启动正常游戏(带NoLogo)");
        LaunchGame();
    }
    private static void LaunchGame() {

        Process me2 = new() {
            StartInfo = new ProcessStartInfo {
                FileName = "launchmod_normal.bat",
                WorkingDirectory = Const.ME2Path,
                UseShellExecute = true,
                CreateNoWindow = true,
            },
        };

        me2.Start();
    }
    private static bool EldenRingIsOpen() {
        Process[] processes = Process.GetProcesses();
        foreach (Process process in processes) {
            if (process.ProcessName is not "eldenring") {
                continue;
            }
            // if (process.HasExited) {
            //     _mwViewModel.DisplayMessage("Elden Ring is closing!");
            // }
            return true;
        }
        return false;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(MainWindowViewModel.FilesReady)
            or nameof(MainWindowViewModel.InProgress)) {
            OnCanExecuteChanged();
        }
    }
}
