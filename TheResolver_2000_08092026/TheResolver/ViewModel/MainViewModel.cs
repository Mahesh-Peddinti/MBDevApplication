using Autodesk.Revit.DB;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using TheResolver.Services;
using TheResolver.Utilities;

namespace TheResolver.ViewModel
{
    public class MainViewModel
    {
        //get Document
        private readonly Document _doc;
        //Constants
        private readonly ClashDetectionService _clashService;
        //private readonly ClashResolution _clashResolution;
        public ObservableCollection<ClashResult> ClashResults { get; set; } = new();
        public ObservableCollection<ClashResult> FilteredResults { get; set; } = new();
        public ClashResult SelectedClash { get; set; }

        //Implement ICommand        
        public ICommand RunDetectionCommand { get; }
        public ICommand ResolveClashCommand { get; }


        public MainViewModel(Document document)
        {
            this._doc = document;
            _clashService = new ClashDetectionService(_doc);
           // _clashResolution = new ClashResolution(_doc);
            RunDetectionCommand = new RelayCommand(RunDetection);
            ResolveClashCommand = new RelayCommand(RunResolveClash);
        }

        private void RunResolveClash()
        {
            //_clashResolution.ResolveClashes();
        }

        private void RunDetection()
        {
            ClashResults.Clear();
            var clashes = _clashService.DetectClashes();
            foreach (var clash in clashes)
            {
                ClashResults.Add(clash);
            }
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredResults.Clear();
            foreach (var clash in ClashResults)
            {
                if (SelectedClash != null && clash != SelectedClash) continue;
                FilteredResults.Add(clash);
            }
        }

    }
}
