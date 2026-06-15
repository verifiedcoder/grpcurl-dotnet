using CommunityToolkit.Mvvm.ComponentModel;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>
///     Base for all Studio view models. Provides change notification via
///     CommunityToolkit.Mvvm's source-generated <see cref="ObservableObject" />; view models
///     declare bindable state with <c>[ObservableProperty]</c> and commands with
///     <c>[RelayCommand]</c> rather than hand-written INotifyPropertyChanged.
/// </summary>
public abstract class ViewModelBase : ObservableObject;
