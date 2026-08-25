using System;
using RevitMepAutomation.Common;
using RevitMepAutomation.Models;

namespace RevitMepAutomation.ViewModels
{
    public class ClashItemViewModel : ViewModelBase
    {
        private readonly ClashItemModel _model;
        private ClashItemModel _snapshot;
        private bool _isSelected;

        public ClashItemModel Model => _model;

        public int Id => _model.Id;

        public string Name
        {
            get => _model.Name;
            set
            {
                if (_model.Name != value)
                {
                    _model.Name = value;
                    OnPropertyChanged();
                }
            }
        }

        public ClashStatus Status
        {
            get => _model.Status;
            set
            {
                if (_model.Status != value)
                {
                    _model.Status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsResolved));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public bool IsResolved => _model.Status == ClashStatus.Resolved;

        public string StatusText => _model.Status switch
        {
            ClashStatus.Resolved => "Resolved",
            ClashStatus.NotResolved => "Not Resolved",
            ClashStatus.Ignored => "Ignored",
            _ => "Pending"
        };

        public double BendRadiusMm
        {
            get => _model.BendRadiusMm;
            set
            {
                if (Math.Abs(_model.BendRadiusMm - value) > 0.001)
                {
                    _model.BendRadiusMm = value;
                    OnPropertyChanged();
                }
            }
        }

        public double BendAngleDeg
        {
            get => _model.BendAngleDeg;
            set
            {
                if (Math.Abs(_model.BendAngleDeg - value) > 0.001)
                {
                    _model.BendAngleDeg = value;
                    OnPropertyChanged();
                }
            }
        }

        public double OffsetSpanMm
        {
            get => _model.OffsetSpanMm;
            set
            {
                if (Math.Abs(_model.OffsetSpanMm - value) > 0.001)
                {
                    _model.OffsetSpanMm = value;
                    OnPropertyChanged();
                }
            }
        }

        public double TopClearanceMm
        {
            get => _model.TopClearanceMm;
            set
            {
                if (Math.Abs(_model.TopClearanceMm - value) > 0.001)
                {
                    _model.TopClearanceMm = value;
                    OnPropertyChanged();
                }
            }
        }

        public RoutingDirection Direction
        {
            get => _model.Direction;
            set
            {
                if (_model.Direction != value)
                {
                    _model.Direction = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDirectionUp));
                    OnPropertyChanged(nameof(IsDirectionDown));
                }
            }
        }

        public bool IsDirectionUp
        {
            get => Direction == RoutingDirection.Up;
            set
            {
                if (value) Direction = RoutingDirection.Up;
            }
        }

        public bool IsDirectionDown
        {
            get => Direction == RoutingDirection.Down;
            set
            {
                if (value) Direction = RoutingDirection.Down;
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string ElementCategory => _model.ElementCategory;
        public string ObstacleCategory => _model.ObstacleCategory;
        public int ElementId => _model.ElementId;
        public int ObstacleId => _model.ObstacleId;
        public string Details => _model.Details;

        public ClashItemViewModel(ClashItemModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _snapshot = model.Clone();
        }

        /// <summary>
        /// Saves current parameters as accepted and marks status as Resolved.
        /// </summary>
        public void Accept()
        {
            Status = ClashStatus.Resolved;
            _snapshot = _model.Clone();
        }

        /// <summary>
        /// Cancels changes by reverting back to the last snapshot.
        /// </summary>
        public void Cancel()
        {
            BendRadiusMm = _snapshot.BendRadiusMm;
            BendAngleDeg = _snapshot.BendAngleDeg;
            OffsetSpanMm = _snapshot.OffsetSpanMm;
            TopClearanceMm = _snapshot.TopClearanceMm;
            Direction = _snapshot.Direction;
            Status = _snapshot.Status;
        }

        public void TakeSnapshot()
        {
            _snapshot = _model.Clone();
        }
    }
}
