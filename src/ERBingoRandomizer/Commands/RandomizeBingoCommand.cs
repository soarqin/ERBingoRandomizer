using ERBingoRandomizer.Randomizer;
using ERBingoRandomizer.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ERBingoRandomizer.Utility;

namespace ERBingoRandomizer.Commands;

public class RandomizeBingoCommand : AsyncCommandBase {
    private readonly MainWindowViewModel _mwViewModel;
    public RandomizeBingoCommand(MainWindowViewModel mwViewModel) {
        _mwViewModel = mwViewModel;
        _mwViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }
    public override bool CanExecute(object? parameter) {
        return !_mwViewModel.InProgress
            && !string.IsNullOrWhiteSpace(_mwViewModel.Path)
            && _mwViewModel.Path.ToLower().EndsWith(Const.ExeName)
            && File.Exists(_mwViewModel.Path);
    }
    protected override async Task ExecuteAsync(object? parameter) {
        if (EldenRingIsOpen()) {
            _mwViewModel.DisplayMessage("艾尔登法环还在运行中，请彻底关闭游戏后再次点击。");
            return;
        }
        _mwViewModel.ListBoxDisplay.Clear();
        _mwViewModel.ListBoxDisplay.Clear();
        var lastSeed = _mwViewModel.LastSeed;
        if (lastSeed == null || lastSeed.Seed != _mwViewModel.Seed || lastSeed.RandomStartupClasses != _mwViewModel.RandomStartupClasses || lastSeed.RandomWeapons != _mwViewModel.RandomWeapons ||
            lastSeed.OpenGraces != _mwViewModel.OpenGraces || lastSeed.ReduceUpgradeMat != _mwViewModel.ReduceUpgradeMat) {
            _mwViewModel.DisplayMessage("正在随机艾尔登法环规则文件");
            _mwViewModel.InProgress = true;
            _mwViewModel.RandoButtonText = "Cancel";
            // _mwViewModel.Path is not null, and is a valid path to eldenring.exe, because of the conditions in CanExecute.
            try
            {
                var rule = new RandomizeRule {
                    Seed = _mwViewModel.Seed,
                    RandomStartupClasses = _mwViewModel.RandomStartupClasses,
                    RandomWeapons = _mwViewModel.RandomWeapons,
                    OpenGraces = _mwViewModel.OpenGraces,
                    ReduceUpgradeMat = _mwViewModel.ReduceUpgradeMat,
                    ReduceUpgradeMatType = _mwViewModel.ReduceUpgradeMatType,
                };
                BingoRandomizer randomizer = await BingoRandomizer.BuildRandomizerAsync(_mwViewModel.Path!, rule, _mwViewModel.CancellationToken);
                await Task.Run(() => randomizer.RandomizeRegulation());
                _mwViewModel.LastSeed = new SeedInfo(randomizer.GetSeed(), Util.GetShaRegulation256Hash(), _mwViewModel.RandomStartupClasses, _mwViewModel.RandomWeapons, _mwViewModel.OpenGraces, _mwViewModel.ReduceUpgradeMat, _mwViewModel.ReduceUpgradeMatType);
                var seedJson = JsonSerializer.Serialize(_mwViewModel.LastSeed);
                await File.WriteAllTextAsync(Config.LastSeedPath, seedJson);
                _mwViewModel.FilesReady = true;
                _mwViewModel.DisplayMessage($"随机完成。种子：{randomizer.GetSeed()}");
            }
            catch (OperationCanceledException)
            {
                _mwViewModel.DisplayMessage("已取消随机");
            }
            finally
            {
                _mwViewModel.RandoButtonText = "Randomize";
                _mwViewModel.InProgress = false;
            }
        }
        _mwViewModel.DisplayMessage("正在用MOD引擎启动艾尔登法环");
        Process me2 = new() {
            StartInfo = new ProcessStartInfo {
                FileName = "modengine2_launcher.exe",
                Arguments = "-t er -c config_bingo.toml -p \"" + _mwViewModel.Path + "\"",
                WorkingDirectory = Const.ME2Path,
                UseShellExecute = true,
                CreateNoWindow = true,
            },
        };
        me2.Start();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(MainWindowViewModel.InProgress)
            or nameof(MainWindowViewModel.Path)
            or nameof(MainWindowViewModel.LastSeed)
            or nameof(MainWindowViewModel.Seed)
            or nameof(MainWindowViewModel.FilesReady)) {
            OnCanExecuteChanged();
        }
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
}
