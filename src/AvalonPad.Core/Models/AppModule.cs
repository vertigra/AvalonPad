using System.Collections.Generic;
using System.Windows.Input;

namespace AvalonPad.Core.Models;

/// <summary>
/// Контракт приложения для оболочки (Shell): название, панели, пункты меню.
/// Ссылки компиляции — вниз, данные — вверх: Shell знает только контракт,
/// конкретные типы приложений Shell в компиляции не видит.
/// </summary>
public interface IAppModule
{
    /// <summary>Заголовок приложения (заголовок главного окна).</summary>
    string AppTitle { get; }

    /// <summary>Панели-инструменты приложения. Панели регистрируются до восстановления workspace.</summary>
    IEnumerable<ToolRegistration> Tools { get; }

    /// <summary>Пункты меню приложения. ParentHeader — имя корневой группы Shell.</summary>
    IEnumerable<MenuRegistration> MenuItems { get; }
}

/// <summary>Регистрация панели-инструмента (панель — singleton в DI).</summary>
public sealed record ToolRegistration(Type ViewModelType);

/// <summary>
/// Пункт меню приложения.
/// Header null — разделитель. ParentHeader — имя корневой группы: «Файл», «Панели инструментов», «Настройки».
/// </summary>
public sealed record MenuRegistration(string? Header, ICommand? Command, string? InputGesture = null, string ParentHeader = "Файл");
