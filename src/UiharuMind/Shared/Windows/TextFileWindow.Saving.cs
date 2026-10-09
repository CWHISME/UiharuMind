using System;
using System.Threading.Tasks;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 文本窗的保存：原编码写回、存不下先问、另存为、换源与关窗前的未保存确认
/// </summary>
public partial class TextFileWindow
{
    /// <summary>
    /// 有未保存改动时三态确认，返回是否可以继续（打开新文件 / 新建空文档共用）
    /// </summary>
    /// <returns>无改动、或用户选择保存/放弃后返回 true；取消（或保存失败）返回 false</returns>
    private async Task<bool> ConfirmDiscardUnsavedAsync(string message)
    {
        if (!_document.IsDirty) return true;
        var choice = await MessageService.ConfirmWithCancelAsync(message, Loc.Text(LangKey.TextFileCloseDirtyTitle));
        if (choice == EConfirmChoice.Cancel) return false;
        if (choice == EConfirmChoice.Yes && !await TrySaveAsync()) return false;
        _document.IsDirty = false; //No：放弃改动
        RefreshDirtyState();
        return true;
    }

    private async Task<bool> TrySaveAsync()
    {
        // 空文档与只读文档没有回写路径，保存即另存为（普通编辑器行为）
        if (_document.FilePath == null || _document.IsReadOnly) return await SaveAsAsync();

        string text = DocumentView.TextView.Text ?? string.Empty;
        if (!await EnsureEncodableAsync(text)) return false;
        if (!await ConfirmOverwriteDiskChangeAsync(_document.FilePath)) return false;
        try
        {
            await TextFileCodec.WriteTextAsync(_document.FilePath, text, _document.Encoding, _document.HasBom, default);
            OnSavedToDisk();
            _document.IsDirty = false;
            RefreshDirtyState();
            RefreshMenuStates();
            MessageService.ShowNotification(Loc.Text(LangKey.TextFileSaved), severity: MessageSeverity.Success);
            return true;
        }
        catch (Exception e)
        {
            MessageService.ShowNotification(
                $"{Loc.Text(LangKey.TextFileSaveFailed)} {e.Message}", severity: MessageSeverity.Error);
            return false;
        }
    }

    /// <summary>
    /// 原编码存不下正文时（Big5、Shift-JIS 与单字节编码才会），先问要不要改存 UTF-8：
    /// 不问就写，存不下的字会被静默写成「?」
    /// </summary>
    /// <param name="text">要保存的正文</param>
    /// <returns>可以接着保存返回 true；用户不改编码返回 false</returns>
    private async Task<bool> EnsureEncodableAsync(string text)
    {
        if (TextFileCodec.CanEncode(_document.Encoding, text)) return true;

        string message = string.Format(Loc.Text(LangKey.TextFileEncodingCannotStoreFormat), _document.EncodingDisplay);
        if (!await MessageService.ConfirmAsync(message)) return false;

        _document.SwitchToUtf8();
        UpdateStatusBar();
        return true;
    }

    /// <summary>
    /// 另存为：空文档也能调，存完路径落定、标题与高亮跟着新扩展名走。
    /// 只读文档的另存为是导出一份副本：窗口照旧看原来那份
    /// </summary>
    /// <returns>存成功返回 true；用户取消或失败返回 false</returns>
    private async Task<bool> SaveAsAsync()
    {
        string text = DocumentView.TextView.Text ?? string.Empty;
        if (!await EnsureEncodableAsync(text)) return false;

        var uri = await App.FilesService.SaveFileAsync(this, _document.GetDefaultSaveName(DocumentView.IsPreview));
        if (uri == null) return false; //用户取消

        try
        {
            await TextFileCodec.WriteTextAsync(uri.LocalPath, text, _document.Encoding, _document.HasBom, default);
        }
        catch (Exception ex)
        {
            MessageService.ShowNotification(
                $"{Loc.Text(LangKey.TextFileSaveFailed)} {ex.Message}", severity: MessageSeverity.Error);
            return false;
        }

        _setting.RememberFile(uri.LocalPath); //存下的文件记一笔，导出的副本也能从「最近打开」找回来
        if (!_document.IsReadOnly)
        {
            _document.MoveTo(uri.LocalPath);
            ResetDiskTracking();
            DocumentView.LinkBaseDirectory = _document.LinkBaseDirectory;
            ApplyGlobalSettings(); //扩展名可能变了，高亮按新路径重算
            RefreshDirtyState();
            RefreshMenuStates();
            UpdateStatusBar();
        }

        MessageService.ShowNotification(Loc.Text(LangKey.TextFileSaved), severity: MessageSeverity.Success);
        return true;
    }

    private async Task ConfirmCloseAsync()
    {
        try
        {
            var choice = await MessageService.ConfirmWithCancelAsync(
                Loc.Text(LangKey.TextFileCloseDirtyConfirm),
                Loc.Text(LangKey.TextFileCloseDirtyTitle));
            if (choice == EConfirmChoice.Cancel) return;
            if (choice == EConfirmChoice.Yes)
            {
                if (!await TrySaveAsync()) return; //保存失败留在原地，别丢数据
                SafeClose();
                return;
            }

            //No：放弃改动
            _document.IsDirty = false;
            RefreshDirtyState();
            SafeClose();
        }
        finally
        {
            _closingPromptOpen = false;
        }
    }
}
