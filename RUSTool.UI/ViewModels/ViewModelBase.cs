using CommunityToolkit.Mvvm.ComponentModel;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 所有 ViewModel 的基类。
/// 继承 CommunityToolkit.Mvvm 的 ObservableObject：属性变更通知与 RelayCommand
/// 都由源生成器产出，派生类只写字段与命令方法。
/// </summary>
public abstract class ViewModelBase : ObservableObject;
