using AvalonPad.Core.AvalonDock.ViewModels;

namespace AvalonPad.Core.Services;

/// <summary>
/// Каталог документов: по имени типа из workspace.json создаёт экземпляр ViewModel.
/// Знание о конкретных типах документов живёт в приложении (реализация каталога),
/// оболочка (Shell) работает только с контрактом.
/// </summary>
public interface IDocumentCatalog
{
    /// <summary>
    /// Создать документ по имени типа. null — тип каталогу неизвестен.
    /// </summary>
    AvalonDocumentViewModel? Create(string documentTypeName);
}
