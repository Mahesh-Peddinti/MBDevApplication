using System;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace TheResolver.DTOs
{
    /// <summary>
    /// One selectable model (host document or Revit link) shown in the
    /// "Clash Check Model" list.
    /// </summary>
    public class ClashModelItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        /// <summary>
        /// Raised whenever <see cref="IsSelected"/> changes so the owning
        /// view model can refresh its selection summary.
        /// </summary>
        public Action SelectionChangedAction { get; set; }

        public string Name { get; set; }

        public Document Document { get; set; }

        public bool IsHost { get; set; }

        /// <summary>
        /// Link instance that brought this document into the host model.
        /// Null for the host document itself.
        /// </summary>
        public ElementId LinkInstanceId { get; set; }

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

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}
