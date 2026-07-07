using CommunityToolkit.Mvvm.Input;
using PalCalc.Model;
using PalCalc.SaveReader;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using PalCalc.UI.ViewModel;
using PalCalc.UI.ViewModel.Inspector;
using PalCalc.UI.ViewModel.Inspector.Search;
using PalCalc.UI.ViewModel.Inspector.Search.Grid;
using PalCalc.UI.ViewModel.Mapped;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class SaveViewerWindowViewModel
    {
        private static ILogger logger = Log.ForContext<SaveViewerWindowViewModel>();

        private static SaveViewerWindowViewModel designerInstance = null;
        public static SaveViewerWindowViewModel DesignerInstance
        {
            get
            {
                if (designerInstance == null)
                {
                    designerInstance = new SaveViewerWindowViewModel();
                    designerInstance.AvailableSaves = new ObservableCollection<SaveGameViewModel>
                    {
                        SaveGameViewModel.DesignerInstance
                    };
                }
                return designerInstance;
            }
        }

        private SaveGameViewModel selectedSave;
        public SaveGameViewModel SelectedSave
        {
            get => selectedSave;
            set
            {
                if (selectedSave != value)
                {
                    selectedSave = value;
                    OnSelectedSaveChanged();
                    OnPropertyChanged(nameof(SelectedSave));
                }
            }
        }

        public ObservableCollection<SaveGameViewModel> AvailableSaves { get; set; } = new ObservableCollection<SaveGameViewModel>();

        private SearchViewModel search;
        public SearchViewModel Search
        {
            get => search;
            set
            {
                search = value;
                OnPropertyChanged(nameof(Search));
            }
        }

        private SaveDetailsViewModel details;
        public SaveDetailsViewModel Details
        {
            get => details;
            set
            {
                details = value;
                OnPropertyChanged(nameof(Details));
            }
        }

        public ILocalizedText WindowTitle { get; private set; }

        public ICommand RefreshCommand { get; }

        public IRelayCommand<IContainerGridSlotViewModel> DeleteSlotCommand { get; }

        public SaveViewerWindowViewModel()
        {
            WindowTitle = new HardCodedText("Pal Save Viewer");
            RefreshCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(OnRefresh);
            DeleteSlotCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<IContainerGridSlotViewModel>(OnDeleteSlot);
        }

        public void Initialize()
        {
            var availableSavesLocations = new List<ISavesLocation>();
            availableSavesLocations.AddRange(DirectSavesLocation.AllLocal);

            try
            {
                var xboxLocations = XboxSavesLocation.FindAll();
                if (xboxLocations.Count > 0) availableSavesLocations.AddRange(xboxLocations);
            }
            catch { }

            var allSaves = new List<SaveGameViewModel>();
            foreach (var sl in availableSavesLocations)
            {
                foreach (var sg in sl.ValidSaveGames)
                {
                    allSaves.Add(new SaveGameViewModel(sl, sg));
                }
            }

            AvailableSaves = new ObservableCollection<SaveGameViewModel>(
                allSaves.OrderByDescending(s => s.LastModified)
            );

            OnPropertyChanged(nameof(AvailableSaves));

            if (AvailableSaves.Count > 0)
                SelectedSave = AvailableSaves[0];
        }

        private void OnSelectedSaveChanged()
        {
            if (SelectedSave == null)
            {
                Search = null;
                Details = null;
                WindowTitle = new HardCodedText("Pal Save Viewer");
                OnPropertyChanged(nameof(WindowTitle));
                return;
            }

            try
            {
                var gameSettings = GameSettingsViewModel.Load(SelectedSave.Value).ModelObject;
                WindowTitle = LocalizationCodes.LC_SAVEWINDOW_TITLE.Bind(SelectedSave.Label);
                OnPropertyChanged(nameof(WindowTitle));

                Search = new SearchViewModel(SelectedSave, gameSettings);
                Details = new SaveDetailsViewModel(SelectedSave.ContainerLocation, SelectedSave.CachedValue);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error loading save data for {saveId}", CachedSaveGame.IdentifierFor(SelectedSave.Value));
                MessageBox.Show($"Error loading save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRefresh()
        {
            if (SelectedSave != null)
            {
                try
                {
                    Storage.ReloadSave(SelectedSave.ContainerLocation, SelectedSave.Value, PalDB.LoadEmbedded(), GameSettingsViewModel.Load(SelectedSave.Value).ModelObject);
                    OnSelectedSaveChanged();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error refreshing save data");
                    MessageBox.Show($"Error refreshing save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnDeleteSlot(IContainerGridSlotViewModel slot)
        {
            if (Search == null) return;

            var subCommands = Search.OwnerTree.AllContainerSources
                .SelectMany(s => s.Container.Grids)
                .Select(g => g.DeleteSlotCommand)
                .Where(cmd => cmd != null)
                .Where(cmd => cmd.CanExecute(slot))
                .ToList();

            foreach (var cmd in subCommands)
                cmd.Execute(slot);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}