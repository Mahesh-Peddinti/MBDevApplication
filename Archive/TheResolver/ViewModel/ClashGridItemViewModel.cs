using System.ComponentModel;
using TheResolver.DTOs;

namespace TheResolver.ViewModel
{   

    public class ClashGridItemViewModel : INotifyPropertyChanged
    {
        
        private bool _isSelected;
        public Action SelectionChangedAction
        {
            get;
            set;
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;

                System.Diagnostics.Debug.WriteLine($"{ClashId} -> {value}");

                OnPropertyChanged(nameof(IsSelected));

                SelectionChangedAction?.Invoke();
            }
        }

        public string ClashId { get; set; }

        public string Element1 { get; set; }

        public string Element2 { get; set; }

        public string Element2ProjectName { get; set; }

        public string Status { get; set; }

        public bool IsResolved { get; set; }

        public ClashInfoDTOs ClashInfo { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }


       
    }
}
