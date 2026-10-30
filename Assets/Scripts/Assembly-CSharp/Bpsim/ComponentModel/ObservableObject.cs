using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Bpsim.ComponentModel
{
	public class ObservableObject : INotifyPropertyChanged, INotifyPropertyChanging
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public event PropertyChangingEventHandler PropertyChanging;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		protected void OnPropertyChanging([CallerMemberName] string propertyName = null)
		{
			this.PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
		}

		protected void SetProperty<T>(ref T property, T value, [CallerMemberName] string propertyName = null)
		{
			if (!EqualityComparer<T>.Default.Equals(property, value))
			{
				OnPropertyChanging(propertyName);
				property = value;
				OnPropertyChanged(propertyName);
			}
		}

		public void Unregister()
		{
			this.PropertyChanged = null;
			this.PropertyChanging = null;
		}
	}
}
