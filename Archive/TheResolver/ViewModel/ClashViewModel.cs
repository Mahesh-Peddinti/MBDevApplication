using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TheResolver.DTOs;
using TheResolver.Services;
using TheResolver.Utilities;

namespace TheResolver.ViewModel
{
    public class ClashViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ClashGridItemViewModel> Clashes {  get; }
                                = new ObservableCollection<ClashGridItemViewModel>();
        
        private bool _suspendPreview;

        private bool _isAllSelected;

        private bool _isUpdatingSelection;

        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (_isAllSelected == value)
                    return;

                _isAllSelected = value;

                _isUpdatingSelection = true;

                foreach (var clash in Clashes)
                {
                    clash.IsSelected = value;
                }

                _isUpdatingSelection = false;

                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(IsAllSelected)));
            }
        }



        private ClashGridItemViewModel _selectedClash;
        public ClashGridItemViewModel SelectedClash
        {
            get => _selectedClash;
            set
            {
                if (_selectedClash == value)
                    return;

                _selectedClash = value;

                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(SelectedClash)));

                UpdateStatus();

                // Automatically preview selected clash
                RaisePreview();     
            }
        }

        private BitmapImage _previewImage;

        public BitmapImage PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage = value;

                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs( nameof(PreviewImage)));
            }
        }

        private string _statusText;

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }


        //Commands 
        public ICommand RunClashDetectionCommand { get; }
        public ICommand AcceptCommand { get; }

        private readonly ClashResolveTool clashResolveTool;
        private readonly ExternalEvent modelEvent;



        public ClashViewModel()
        {
            clashResolveTool = new ClashResolveTool(this);

            modelEvent = ExternalEvent.Create(clashResolveTool);

            RunClashDetectionCommand =
                new RelayCommand(RunClashDetection);

            AcceptCommand =
                new RelayCommand(RunClashResolution);
        }

        private void RunClashDetection()
        {
            
            clashResolveTool.Request =
                ModelRequest.RunClashDetection;

            modelEvent.Raise();
        }
        private void RunClashResolution()
        {
            var selectedClashes =
                Clashes
                .Where(x => x.IsSelected)
                .ToList();

            if (!selectedClashes.Any())
                return;

            clashResolveTool.SelectedClashes = selectedClashes;

            clashResolveTool.Request =
                ModelRequest.ResolveSelected;

            modelEvent.Raise();
        }

        public void LoadClashes(List<ClashInfoDTOs> clashes)
        {
            _suspendPreview = true;

            Clashes.Clear();


            _isAllSelected = false;
            PropertyChanged?.Invoke(this,
                new PropertyChangedEventArgs(nameof(IsAllSelected)));

            int clashID = 1 ;

            foreach (var clash in clashes)
            {

                var row =
                     new ClashGridItemViewModel
                     {
                         ClashId = $"Clash - {clashID++}",

                         Element1 = clash.Tray?.Category?.Name,

                         Element2 = clash.ClashElement?.Category?.Name,

                         Element2ProjectName =
                             clash.ClashElement?.Document?.Title
                             ?? "Current Model",

                         ClashInfo = clash,

                         IsResolved = false,

                         SelectionChangedAction =  UpdateSelectAllState
                     };

                    Clashes.Add(row);
            }
            if (Clashes.Count > 0)
                SelectedClash = Clashes[0];
            _suspendPreview = false;
        }

        private void UpdateStatus()
        {
            if (SelectedClash == null)
                return;

            StatusText =
                SelectedClash.IsResolved
                ? "Resolved"
                : "Not Resolved";
        }

        private void RaisePreview()
        {
            if (_suspendPreview)
                return;

            if (SelectedClash == null)
                return;

            clashResolveTool.Request =
                ModelRequest.PreviewRoute;

            modelEvent.Raise();
        }



        public void UpdateSelectAllState()
        {
            if (_isUpdatingSelection)
                return;

            bool allSelected =
                Clashes.Count > 0 &&
                Clashes.All(x => x.IsSelected);

            if (_isAllSelected != allSelected)
            {
                _isAllSelected = allSelected;

                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(IsAllSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}