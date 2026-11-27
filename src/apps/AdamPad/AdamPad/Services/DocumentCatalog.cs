using AvalonPad.Core.AvalonDock.ViewModels;
using AvalonPad.Core.Services;
using AvalonPad.Shell.ViewModels;
using AdamPad.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace AdamPad.Services;

/// <summary>
/// Каталог документов приложения: по имени типа из workspace.json создаёт экземпляр.
/// Знание о конкретных документах живёт здесь, а не в PersistenceService (Shell).
/// Новые типы документов регистрируются методом Register.
/// </summary>
public sealed class DocumentCatalog : IDocumentCatalog
{
    private readonly Dictionary<string, Func<AvalonDocumentViewModel?>> mFactories = new(StringComparer.Ordinal);
    private readonly IServiceProvider mServices;

    public DocumentCatalog(IServiceProvider services)
    {
        mServices = services;

        Register(typeof(EditorTabViewModel).FullName!, () => mServices.GetRequiredService<EditorTabViewModel>());
        Register(typeof(SettingsViewModel).FullName!, () => mServices.GetRequiredService<SettingsViewModel>());
    }

    /// <summary>Зарегистрировать фабрику документа по полному имени типа.</summary>
    public void Register(string documentTypeName, Func<AvalonDocumentViewModel?> factory) =>
        mFactories[documentTypeName] = factory;

    /// <summary>Создать документ по имени типа из workspace.json. null — тип неизвестен.</summary>
    public AvalonDocumentViewModel? Create(string documentTypeName) =>
        mFactories.TryGetValue(documentTypeName, out var factory) ? factory() : null;
}
