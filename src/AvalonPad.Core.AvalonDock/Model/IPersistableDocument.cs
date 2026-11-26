namespace AvalonPad.Core.AvalonDock.Model;

/// <summary>
/// Документ с собственным динамическим состоянием в workspace.json.
/// Реализуется конкретными документами; PersistenceService знает только контракт.
/// Общие поля (ContentId, Title, IsActive, IsModified) сохраняет/восстанавливает PersistenceService.
/// </summary>
public interface IPersistableDocument
{
    /// <summary>Записать свои динамические поля в сохраняемое состояние.</summary>
    void SaveState(WorkspaceDocument state);

    /// <summary>Восстановить свои динамические поля из сохранённого состояния.</summary>
    void LoadState(WorkspaceDocument state);
}
