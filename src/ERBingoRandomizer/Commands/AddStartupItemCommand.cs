using System;
using ERBingoRandomizer.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ERBingoRandomizer.Commands;

public class AddStartupItemCommand : ICommand {
    private readonly MainWindowViewModel _mwViewModel;
    private readonly int _category;
    protected AddStartupItemCommand(MainWindowViewModel mwViewModel, int category)
    {
        _mwViewModel = mwViewModel;
        _category = category;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        if (parameter is not MenuItem mi)
            return;
        int id = int.Parse((string)mi.Tag);
        _mwViewModel.AddStartupItem((string)mi.Header, id, _category);
    }
}

public class AddStartupWeaponCommand : AddStartupItemCommand
{
    public AddStartupWeaponCommand(MainWindowViewModel mwViewModel) : base(mwViewModel, 2)
    {
    }

}

public class AddStartupAccessoryCommand : AddStartupItemCommand
{
    public AddStartupAccessoryCommand(MainWindowViewModel mwViewModel) : base(mwViewModel, 4)
    {
    }
}

public class RemoveStartupItemCommand : CommandBase
{
    private readonly MainWindowViewModel _mwViewModel;
    public RemoveStartupItemCommand(MainWindowViewModel mwViewModel)
    {
        _mwViewModel = mwViewModel;
        _mwViewModel.PropertyChanged += (o, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.StartupItemSelectedIndex))
            {
                OnCanExecuteChanged();
            }
        };
    }

    public override bool CanExecute(object? parameter)
    {
        return _mwViewModel.StartupItemSelectedIndex >= 0;
    }

    public override void Execute(object? parameter)
    {
        _mwViewModel.RemoveStartupItem(_mwViewModel.StartupItemSelectedIndex);
    }
}