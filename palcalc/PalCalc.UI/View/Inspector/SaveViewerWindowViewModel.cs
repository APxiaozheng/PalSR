using CommunityToolkit.Mvvm.ComponentModel;
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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class SaveViewerWindowViewModel : ObservableObject
    {
        private static ILogger logger = Log.ForContext<SaveViewerWindowViewModel>();
        private static PalDB db;

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

        private AppSettings settings;

        // Language switching
        public List<TranslationLocaleViewModel> Locales { get; } =
            Enum.GetValues<TranslationLocale>()
                .Select(l => new TranslationLocaleViewModel(l))
                .ToList();

        // Save selection
        private SaveGameViewModel selectedSave;
        public SaveGameViewModel SelectedSave
        {
            get => selectedSave;
            set
            {
                if (SetProperty(ref selectedSave, value))
                {
                    OnSelectedSaveChanged();
                }
            }
        }

        public ObservableCollection<SaveGameViewModel> AvailableSaves { get; set; } = new ObservableCollection<SaveGameViewModel>();

        // Loading state
        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string loadingMessage;

        // Search and Details
        private SearchViewModel search;
        public SearchViewModel Search
        {
            get => search;
            set => SetProperty(ref search, value);
        }

        private SaveDetailsViewModel details;
        public SaveDetailsViewModel Details
        {
            get => details;
            set => SetProperty(ref details, value);
        }

        [ObservableProperty]
        private ILocalizedText windowTitle = new HardCodedText("Pal Save Viewer");

        public ICommand RefreshCommand { get; }
        public IRelayCommand<IContainerGridSlotViewModel> DeleteSlotCommand { get; }

        public SaveViewerWindowViewModel()
        {
            RefreshCommand = new RelayCommand(OnRefresh);
            DeleteSlotCommand = new RelayCommand<IContainerGridSlotViewModel>(OnDeleteSlot);
        }

        public void Initialize(Dispatcher dispatcher)
        {
            // Initialize storage
            Storage.Init();

            // Load app settings
            AppSettings.Current = settings = Storage.LoadAppSettings();
            settings.SolverSettings ??= new SerializableSolverSettings();

            // Set locale from saved settings
            Translator.CurrentLocale = settings.Locale;
            Translator.LocaleUpdated += () =>
            {
                if (settings.Locale != Translator.CurrentLocale)
                {
                    settings.Locale = Translator.CurrentLocale;
                    Storage.SaveAppSettings(settings);
                }
            };

            // Load save files
            LoadAvailableSaves();
        }

        private void LoadAvailableSaves()
        {
            var availableSavesLocations = new List<ISavesLocation>();
            availableSavesLocations.AddRange(DirectSavesLocation.AllLocal);

            try
            {
                var xboxLocations = XboxSavesLocation.FindAll();
                if (xboxLocations.Count > 0)
                    availableSavesLocations.AddRange(xboxLocations);
                else
                    availableSavesLocations.Add(new XboxSavesLocation());
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

            // Add manual saves from settings
            var manualSaves = settings.ExtraSaveLocations
                .Where(loc => System.IO.Directory.Exists(loc))
                .Select(saveFolder => new StandardSaveGame(saveFolder))
                .ToList();
            foreach (var loc in settings.ExtraSaveLocations.Where(loc => !System.IO.Directory.Exists(loc)).ToList())
            {
                settings.ExtraSaveLocations.Remove(loc);
            }
            Storage.SaveAppSettings(settings);

            // Add fake saves
            var fakeSaves = settings.FakeSaveNames.Select(FakeSaveGame.Create).ToList();

            allSaves = allSaves.OrderByDescending(s => s.LastModified).ToList();

            AvailableSaves = new ObservableCollection<SaveGameViewModel>(allSaves);
            OnPropertyChanged(nameof(AvailableSaves));

            // Auto-select save
            if (AvailableSaves.Count > 0)
            {
                if (settings.SelectedGameIdentifier != null)
                {
                    var match = AvailableSaves.FirstOrDefault(s => CachedSaveGame.IdentifierFor(s.Value) == settings.SelectedGameIdentifier);
                    if (match != null)
                        SelectedSave = match;
                    else
                        SelectedSave = AvailableSaves[0];
                }
                else
                {
                    SelectedSave = AvailableSaves[0];
                }
            }
        }

        private void OnSelectedSaveChanged()
        {
            if (SelectedSave == null)
            {
                Search = null;
                Details = null;
                WindowTitle = new HardCodedText("Pal Save Viewer");
                return;
            }

            // Save selection for next startup
            settings.SelectedGameIdentifier = CachedSaveGame.IdentifierFor(SelectedSave.Value);
            Storage.SaveAppSettings(settings);

            try
            {
                // Ensure cached save data is loaded
                if (db == null) db = PalDB.LoadEmbedded();
                var gameSettings = GameSettingsViewModel.Load(SelectedSave.Value).ModelObject;

                WindowTitle = new HardCodedText($"Pal Save Viewer - {SelectedSave.Label.Value}");

                Search = new SearchViewModel(SelectedSave, gameSettings);
                Details = new SaveDetailsViewModel(SelectedSave.ContainerLocation, SelectedSave.CachedValue);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error loading save data for {saveId}", CachedSaveGame.IdentifierFor(SelectedSave.Value));
                System.Windows.MessageBox.Show($"Error loading save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRefresh()
        {
            if (SelectedSave == null) return;

            try
            {
                if (db == null) db = PalDB.LoadEmbedded();
                var gameSettings = GameSettingsViewModel.Load(SelectedSave.Value).ModelObject;
                Storage.ReloadSave(SelectedSave.ContainerLocation, SelectedSave.Value, db, gameSettings);
                OnSelectedSaveChanged();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error refreshing save data");
                System.Windows.MessageBox.Show($"Error refreshing save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}