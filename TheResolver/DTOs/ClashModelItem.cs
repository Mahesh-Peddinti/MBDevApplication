using System;
using System.Collections.ObjectModel;
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
        private bool _isExpanded;

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

        /// <summary>
        /// Checkable categories available within this model.
        /// Populated after the user clicks "Load".
        /// </summary>
        public ObservableCollection<CategoryItem> Categories { get; }
            = new ObservableCollection<CategoryItem>();

        /// <summary>
        /// Whether the category tree under this model is expanded.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }

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

        /// <summary>
        /// Convenience accessor for the categories the user ticked.
        /// </summary>
        public ObservableCollection<BuiltInCategory> SelectedCategories
        {
            get
            {
                var selected = new ObservableCollection<BuiltInCategory>();

                foreach (var cat in Categories)
                    if (cat.IsSelected)
                        selected.Add(cat.Category);

                return selected;
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
