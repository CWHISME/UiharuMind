using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SharpHook.Data;
using UiharuMind.Core.Input;
using UiharuMind.Services;

namespace UiharuMind.Views.Windows.Common;

public partial class KeySelectionWindow : Window
{
    private readonly bool _allowModifiers;
    private IDisposable? _shortcutSuspendScope;

    public KeySelectionWindow()
        : this(true)
    {
    }

    private KeySelectionWindow(bool allowModifiers)
    {
        _allowModifiers = allowModifiers;
        InitializeComponent();
        InstructionText.Text = LocalizationManager.Instance.GetString(
            allowModifiers ? "KeySelectionShortcutInstruction" : "KeySelectionSingleInstruction");
    }

    public static Task<KeySelectionResult?> ShowShortcutDialog(Window owner)
    {
        return new KeySelectionWindow(true).ShowDialog<KeySelectionResult?>(owner);
    }

    public static Task<KeySelectionResult?> ShowSingleKeyDialog(Window owner)
    {
        return new KeySelectionWindow(false).ShowDialog<KeySelectionResult?>(owner);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _shortcutSuspendScope = InputManager.Instance.SuspendRegisteredShortcuts();
        InputManager.Instance.EventOnKeyDown += OnKeyDown;
        KeyDisplayText.Text = LocalizationManager.Instance.GetString("KeySelectionWaiting");
        Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        InputManager.Instance.EventOnKeyDown -= OnKeyDown;
        _shortcutSuspendScope?.Dispose();
        _shortcutSuspendScope = null;
        base.OnClosed(e);
    }

    private void OnKeyDown(KeyCode keyCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (keyCode == KeyCode.VcEscape)
            {
                Close(null);
                return;
            }

            if (ShortcutGestureParser.IsModifierKey(keyCode))
            {
                KeyDisplayText.Text = ShortcutGestureParser.ToDisplayString(KeyCode.VcUndefined, GetPressedModifiers());
                return;
            }

            var modifiers = _allowModifiers ? GetPressedModifiers() : new List<KeyCode>();
            var result = new KeySelectionResult(keyCode, modifiers);
            KeyDisplayText.Text = result.DisplayText;
            Close(result);
        });
    }

    private static List<KeyCode> GetPressedModifiers()
    {
        var modifiers = new List<KeyCode>();
        AddIfPressed(KeyCode.VcLeftControl, KeyCode.VcRightControl);
        AddIfPressed(KeyCode.VcLeftAlt, KeyCode.VcRightAlt);
        AddIfPressed(KeyCode.VcLeftShift, KeyCode.VcRightShift);
        AddIfPressed(KeyCode.VcLeftMeta, KeyCode.VcRightMeta);
        return modifiers;

        void AddIfPressed(KeyCode left, KeyCode right)
        {
            if (InputManager.Instance.IsPressed(left) || InputManager.Instance.IsPressed(right))
            {
                modifiers.Add(left);
            }
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}

public record KeySelectionResult(KeyCode MainKeyCode, List<KeyCode> ModifierKeyCodes)
{
    public string DisplayText => ShortcutGestureParser.ToDisplayString(MainKeyCode, ModifierKeyCodes);
}
