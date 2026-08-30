using System;
using System.ComponentModel;
using TheResolver.DTOs;

namespace TheResolver.ViewModel
{
    /// <summary>
    /// One row in the clash grid.
    /// </summary>
    public class ClashGridItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isResolved;
        private bool _resolveFailed;

        /// <summary>
        /// Raised whenever <see cref="IsSelected"/> changes so the owning
        /// view model can refresh the header "select all" checkbox.
        /// </summary>
        public Action SelectionChangedAction { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;

                OnPropertyChanged(nameof(IsSelected));

                SelectionChangedAction?.Invoke();
            }
        }

        public string ClashId { get; set; }

        public string Element1 { get; set; }

        public string Element1Id { get; set; }

        public string Element2 { get; set; }

        public string Element2Id { get; set; }

        public string Element2ProjectName { get; set; }

        public ClashInfoDTOs ClashInfo { get; set; }

        /// <summary>
        /// True once a bypass route has been created for this clash.
        /// </summary>
        public bool IsResolved
        {
            get => _isResolved;
            set
            {
                if (_isResolved == value)
                    return;

                _isResolved = value;

                if (value)
                    _resolveFailed = false;

                RaiseStatusChanged();
            }
        }

        /// <summary>
        /// True when a resolve attempt ran and could not produce a route.
        /// </summary>
        public bool ResolveFailed
        {
            get => _resolveFailed;
            set
            {
                if (_resolveFailed == value)
                    return;

                _resolveFailed = value;

                if (value)
                    _isResolved = false;

                RaiseStatusChanged();
            }
        }

        public string Status
        {
            get
            {
                if (_isResolved)
                    return "Resolved";

                if (_resolveFailed)
                    return "Failed";

                return "Not Resolved";
            }
        }

        /// <summary>
        /// Bound directly to a Brush property; WPF converts the hex string.
        /// </summary>
        public string StatusColor
        {
            get
            {
                if (_isResolved)
                    return "#2E7D32";

                if (_resolveFailed)
                    return "#B71C1C";

                return "#616161";
            }
        }

        private void RaiseStatusChanged()
        {
            OnPropertyChanged(nameof(IsResolved));
            OnPropertyChanged(nameof(ResolveFailed));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusColor));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}
