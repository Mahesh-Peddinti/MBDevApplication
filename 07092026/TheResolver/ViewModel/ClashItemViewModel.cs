using System.ComponentModel;
using TheResolver.DTOs;

namespace TheResolver.ViewModel
{
    public class ClashItemViewModel : INotifyPropertyChanged
    {
        private bool _isResolved;

        public string Name { get; set; }

        public ClashInfoDTOs ClashInfo { get; set; }

        public bool IsResolved
        {
            get => _isResolved;
            set
            {
                _isResolved = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(IsResolved)));

                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(StatusText)));

                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(StatusColor)));
            }
        }

        

        public string StatusText =>
            IsResolved ? "Resolved" : "Not Resolved";

        public string StatusColor =>
            IsResolved ? "#2E7D32" : "#B71C1C";

        public event PropertyChangedEventHandler PropertyChanged;
    }
}