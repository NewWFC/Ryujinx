using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.UI.ViewModels;
using System;

namespace Ryujinx.Ava.UI.Views.Main;

public partial class MainViewControls : UserControl
{
    public MainWindowViewModel ViewModel;
    
    public MainViewControls()
    {
        InitializeComponent();
    }

    public void Sort_Checked(object sender, RoutedEventArgs args)
    {
        if (sender is RadioButton button)
        {
            var sort = Enum.Parse<ApplicationSort>(button.Tag.ToString());
            ViewModel.Sort(sort);
        }
    }
    
    public void Order_Checked(object sender, RoutedEventArgs args)
    {
        if (sender is RadioButton button)
        {
            var tag = button.Tag.ToString();
            ViewModel.Sort(tag != "Descending");
        }
    }
    
    private void SearchBox_OnKeyUp(object sender, KeyEventArgs e)
    {
        ViewModel.SearchText = SearchBox.Text;
    }
}