using BdoGrindTracker.App.Overlay;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Components;

public partial class OverlayEditor
{
    private OverlayHotkey _toggleOverlayDraft = OverlayHotkey.DefaultToggleOverlay;
    private OverlayHotkey _toggleInteractionDraft = OverlayHotkey.DefaultToggleInteraction;
    private bool _hotkeysEnabledDraft, _hotkeyBusy, _hotkeyEditorOpen;
    private string? _hotkeyFormError;
    private string? _selectedTemplateId, _templateReplaceId, _templateFormError;
    private string _templateNameDraft = "";
    private OverlaySettings? _templateLayoutDraft;
    private OverlayTemplate? _templateToDelete;
    private bool _templateBusy, _templateSaveOpen;

    private OverlayTemplate? SelectedTemplate => Overlay.Templates.FirstOrDefault(t => t.Id == _selectedTemplateId);
    private OverlayTemplate? TemplateToReplace => _templateReplaceId is not null
        ? Overlay.Templates.FirstOrDefault(t => t.Id == _templateReplaceId)
        : Overlay.Templates.FirstOrDefault(t => string.Equals(t.Name, _templateNameDraft.Trim(), StringComparison.OrdinalIgnoreCase));
    private string? HotkeyValidationError => !_toggleOverlayDraft.IsValid || !_toggleInteractionDraft.IsValid
        ? "Wähle eine Kombination mit Strg, Alt, Umschalt oder Win. F1–F11 gehen auch ohne Zusatztaste."
        : _toggleOverlayDraft == _toggleInteractionDraft ? "Die beiden Aktionen benötigen unterschiedliche Tastenkürzel." : null;

    private async Task OpenHotkeyEditor()
    {
        if (_disposed || _saving || _hotkeyBusy) return;
        _toggleOverlayDraft = _settings.ToggleOverlayHotkey;
        _toggleInteractionDraft = _settings.ToggleInteractionHotkey;
        _hotkeysEnabledDraft = _settings.HotkeysEnabled;
        _hotkeyFormError = null;
        _hotkeyEditorOpen = true;
        await ShowFeatureDialog("overlay-hotkeys-edit");
    }

    private async Task CloseHotkeyEditor()
    {
        if (_hotkeyBusy) return;
        _hotkeyEditorOpen = false;
        await JS.InvokeVoidAsync("grindcrest.closeDialog", "overlay-hotkeys-edit");
    }

    private void ResetHotkeyDraft()
    {
        _toggleOverlayDraft = OverlayHotkey.DefaultToggleOverlay;
        _toggleInteractionDraft = OverlayHotkey.DefaultToggleInteraction;
        _hotkeyFormError = null;
    }

    private async Task SaveHotkeys()
    {
        if (_disposed || !_hotkeyEditorOpen || _hotkeyBusy || _saving || HotkeyValidationError is not null) return;
        _hotkeyBusy = true;
        try
        {
            await Change(s => s with
            {
                HotkeysEnabled = _hotkeysEnabledDraft,
                ToggleOverlayHotkey = _toggleOverlayDraft,
                ToggleInteractionHotkey = _toggleInteractionDraft,
            });
            _hotkeyFormError = _error;
            if (_error is null)
            {
                _hotkeyEditorOpen = false;
                await JS.InvokeVoidAsync("grindcrest.closeDialog", "overlay-hotkeys-edit");
            }
        }
        finally { _hotkeyBusy = false; }
    }

    private async Task OpenTemplateSave(OverlayTemplate? replace)
    {
        if (_disposed || _saving || _templateBusy) return;
        _templateReplaceId = replace?.Id;
        _templateNameDraft = replace?.Name ?? "";
        _templateLayoutDraft = OverlayLayout.Normalize(_settings);
        _templateFormError = null;
        _templateSaveOpen = true;
        await ShowFeatureDialog("overlay-template-save");
    }

    private async Task CloseTemplateSave()
    {
        if (_templateBusy) return;
        _templateSaveOpen = false;
        _templateLayoutDraft = null;
        _templateReplaceId = null;
        await JS.InvokeVoidAsync("grindcrest.closeDialog", "overlay-template-save");
    }

    private async Task SaveTemplate()
    {
        if (_disposed || !_templateSaveOpen || _templateBusy || _templateLayoutDraft is not { } layout ||
            string.IsNullOrWhiteSpace(_templateNameDraft)) return;
        _templateBusy = true;
        try
        {
            var result = await Overlay.SaveTemplateAsync(_templateNameDraft, layout, _templateReplaceId ?? TemplateToReplace?.Id);
            _templateFormError = result.Error;
            if (result.Succeeded)
            {
                _selectedTemplateId = Overlay.Templates.FirstOrDefault(t =>
                    string.Equals(t.Name, _templateNameDraft.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
                _templateSaveOpen = false;
                _templateLayoutDraft = null;
                _templateReplaceId = null;
                await JS.InvokeVoidAsync("grindcrest.closeDialog", "overlay-template-save");
            }
        }
        catch (Exception exception) { _templateFormError = "Vorlage nicht gespeichert: " + exception.Message; }
        finally { _templateBusy = false; }
    }

    private async Task ApplySelectedTemplate()
    {
        if (_disposed || _saving || _confirmingLayout || _pendingLayoutChange is not null || SelectedTemplate is not { } template) return;
        if (_settings.Widgets.Count > 0)
        {
            await RequestLayoutConfirmation(new(null, template));
            return;
        }
        _selectedId = null;
        _itemSearch = "";
        await Change(template.ApplyTo);
    }

    private async Task RequestTemplateDelete()
    {
        if (_disposed || _templateBusy || SelectedTemplate is not { } template) return;
        _templateToDelete = template;
        _templateFormError = null;
        await ShowFeatureDialog("overlay-template-delete");
    }

    private async Task CloseTemplateDelete()
    {
        if (_templateBusy) return;
        _templateToDelete = null;
        await JS.InvokeVoidAsync("grindcrest.closeDialog", "overlay-template-delete");
    }

    private async Task DeleteTemplate()
    {
        if (_disposed || _templateBusy || _templateToDelete is not { } template) return;
        _templateBusy = true;
        try
        {
            var result = await Overlay.DeleteTemplateAsync(template.Id);
            _templateFormError = result.Error;
            if (result.Succeeded)
            {
                if (_selectedTemplateId == template.Id) _selectedTemplateId = null;
                _templateToDelete = null;
                await JS.InvokeVoidAsync("grindcrest.closeDialog", "overlay-template-delete");
            }
        }
        catch (Exception exception) { _templateFormError = "Vorlage nicht gelöscht: " + exception.Message; }
        finally { _templateBusy = false; }
    }

    private async Task ShowFeatureDialog(string id)
    {
        StateHasChanged();
        try { await JS.InvokeVoidAsync("grindcrest.showDialog", id); }
        catch (JSException exception) { _error = "Der Dialog konnte nicht geöffnet werden. " + exception.Message; }
    }
}
