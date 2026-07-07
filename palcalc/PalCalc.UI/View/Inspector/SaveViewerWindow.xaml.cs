using AdonisUI.Controls;
using PalCalc.UI.ViewModel.Inspector;
using PalCalc.UI.ViewModel.Inspector.Search;
using System;
using System.Windows;
using System.Windows.Controls;

namespace PalCalc.UI.View.Inspector
{
    public partial class SaveViewerWindow : AdonisWindow
    {
        public SaveViewerWindow()
        {
            InitializeComponent();
            DataContext = new SaveViewerWindowViewModel();

            Loaded += (s, e) =>
            {
                ((SaveViewerWindowViewModel)DataContext).Initialize();
            };
        }

        public SaveViewerWindowViewModel ViewModel => DataContext as SaveViewerWindowViewModel;

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ViewModel?.Search == null) return;

            ViewModel.Search.OwnerTree.SelectedNode = e.NewValue as IOwnerTreeNode;
        }
    }
}
