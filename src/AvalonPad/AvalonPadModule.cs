using AvalonPad.Core.AvalonDock.Services;
using AvalonPad.Core.Models;
using AvalonPad.Core.Services;
using AvalonPad.Factories;
using AvalonPad.Shell.ViewModels;
using AvalonPad.ViewModels;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvalonPad;

/// <summary>
/// Модуль прототипа AvalonPad: приложение с текстовым редактором.
/// Всё знание о конкретных документах и командах — здесь, Shell работает только с контрактом.
/// </summary>
public sealed partial class AvalonPadModule : IAppModule
{
    private readonly IEditorTabViewModelFactory mEditorFactory;
    private readonly IFileService mFileService;
    private readonly IDockWindowService mDockWindowService;
    private readonly IToolboxService mToolboxService;
    private readonly ILogService<AvalonPadModule> mLogService;
    private readonly IDialogService mDialogService;

    public AvalonPadModule(
        IEditorTabViewModelFactory editorFactory,
        IFileService fileService,
        IDockWindowService dockWindowService,
        IToolboxService toolboxService,
        ILogService<AvalonPadModule> logService,
        IDialogService dialogService)
    {
        mEditorFactory = editorFactory;
        mFileService = fileService;
        mDockWindowService = dockWindowService;
        mToolboxService = toolboxService;
        mLogService = logService;
        mDialogService = dialogService;
    }

    public string AppTitle => "AvalonPad";

    public IEnumerable<ToolRegistration> Tools =>
    [
        new(typeof(PropertiesToolboxViewModel)),
        new(typeof(OutputToolboxViewModel)),
    ];

    public IEnumerable<MenuRegistration> MenuItems =>
    [
        new("Новый документ", NewDocumentCommand, "Ctrl+N", "Файл"),
        new("Открыть файл...", OpenFileCommand, "Ctrl+O", "Файл"),
        new("Свойства", TogglePropertiesCommand, ParentHeader: "Панели инструментов"),
        new("Вывод", ToggleOutputCommand, ParentHeader: "Панели инструментов"),
    ];

    [RelayCommand]
    private void NewDocument()
    {
        var doc = mEditorFactory.CreateNew("Новый документ", $"Содержимое документа.\r\nСоздан: {DateTime.Now:G}");

        mDockWindowService.OpenDocument(doc);
        mDockWindowService.ActivateDocument(doc);

        mLogService.LogInformation($"Открыт документ: {doc.Title}");
    }

    [RelayCommand]
    private void OpenFile()
    {
        var path = mFileService.ShowOpenDialog();

        if (path is null)
            return;

        // 1. Ищем среди открытых документов, приводя к EditorTabViewModel
        var existing = mDockWindowService.Documents.OfType<EditorTabViewModel>().FirstOrDefault(d => d.FilePath == path);

        if (existing != null)
        {
            // Если уже открыт — активируем через сервис
            mDockWindowService.ActivateDocument(existing);
            mLogService.LogInformation($"Активирован существующий: {path}");
            return;
        }

        try
        {
            EditorTabViewModel doc = mEditorFactory.CreateFromFile(path);

            mDockWindowService.OpenDocument(doc);
            mDockWindowService.ActivateDocument(doc);

            mLogService.LogInformation($"Открыт файл: {path}");
        }
        catch (Exception ex)
        {
            mLogService.LogError("Не удалось открыть файл", ex);
            mDialogService.ShowInformation($"Не удалось открыть файл: {ex.Message}", "Ошибка");
        }
    }

    [RelayCommand]
    private void ToggleProperties() => mToolboxService.Toggle<PropertiesToolboxViewModel>();

    [RelayCommand]
    private void ToggleOutput() => mToolboxService.Toggle<OutputToolboxViewModel>();
}
