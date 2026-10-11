using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace GuessMelody.Wpf.Input
{
    /// <summary>
    /// Глобальные хоткеи внутри приложения через WinAPI RegisterHotKey.
    /// - Работает, даже если хоткеи добавляют до создания HWND (deferred).
    /// - Само перерегистрирует всё при смене HWND (окно пересоздали).
    /// - Снимает регистрацию при закрытии окна.
    /// </summary>
    public sealed class GlobalHotkeyManager : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;

        private readonly Window _owner;
        private IntPtr _hWnd = IntPtr.Zero;
        private HwndSource _source;

        private readonly Dictionary<int, Action> _actions = new Dictionary<int, Action>();
        private readonly Dictionary<int, (ModifierKeys mods, Key key)> _registrations
            = new Dictionary<int, (ModifierKeys, Key)>();

        // Зарегистрировано ли id в текущем HWND
        private readonly HashSet<int> _registered = new HashSet<int>();

        private int _nextId = 1;
        private bool _disposed;

        private EventHandler _onSourceInitialized;
        private EventHandler _onClosed;

        public GlobalHotkeyManager(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

            _onSourceInitialized = (_, __) => Attach();
            _onClosed = (_, __) => DetachFromWindow();

            _owner.SourceInitialized += _onSourceInitialized;
            _owner.Closed += _onClosed;

            // Если окно уже инициализировано (сервис создан позже) — Attach сразу.
            if (_owner.IsLoaded ||
                PresentationSource.FromVisual(_owner) is HwndSource)
            {
                Attach();
            }
        }

        // =============================================================
        // Регистрация / снятие
        // =============================================================
        public bool Register(ModifierKeys modifiers, Key key, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            int id = _nextId++;
            _registrations[id] = (modifiers, key);
            _actions[id] = action;

            // Если HWND ещё нет — оставляем в deferred, Attach зарегистрирует.
            if (_hWnd == IntPtr.Zero)
            {
                Debug.WriteLine($"[Hotkey] Deferred id={id} {modifiers}+{key}");
                return true;
            }

            return TryRegisterOnHwnd(id, modifiers, key, isDeferred: false);
        }

        private bool TryRegisterOnHwnd(int id, ModifierKeys mods, Key key, bool isDeferred)
        {
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            uint m = (uint)mods;

            if (RegisterHotKey(_hWnd, id, m, vk))
            {
                _registered.Add(id);
                Debug.WriteLine($"[Hotkey] Registered{(isDeferred ? " (deferred)" : "")} " +
                                $"id={id} {mods}+{key}");
                return true;
            }

            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[Hotkey] FAILED{(isDeferred ? " (deferred)" : "")} " +
                            $"id={id} {mods}+{key}, err={err} " +
                            $"({DescribeError(err)})");

            // Не смогли зарегистрировать — чистим словари, чтобы не оставлять «мусор».
            _actions.Remove(id);
            _registrations.Remove(id);
            _registered.Remove(id);
            return false;
        }

        private static string DescribeError(int err)
        {
            switch (err)
            {
                case 1409: return "ERROR_HOTKEY_ALREADY_REGISTERED — комбинация занята";
                case 5: return "ERROR_ACCESS_DENIED — нет прав";
                case 87: return "ERROR_INVALID_PARAMETER — неверный VK/модификатор";
                default: return "см. winerror.h";
            }
        }

        public void UnregisterAll()
        {
            if (_hWnd == IntPtr.Zero) return;

            foreach (var id in new List<int>(_registered))
            {
                try { UnregisterHotKey(_hWnd, id); }
                catch (Exception ex) { Debug.WriteLine($"[Hotkey] Unregister error: {ex}"); }
            }
            _registered.Clear();
        }

        // =============================================================
        // Attach / Detach — привязка к окну
        // =============================================================
        private void Attach()
        {
            if (_disposed) return;

            var hwnd = new WindowInteropHelper(_owner).Handle;
            if (hwnd == IntPtr.Zero)
            {
                Debug.WriteLine("[Hotkey] Attach: HWND == 0, ждём SourceInitialized.");
                return;
            }

            // Если HWND не изменился — ничего не делаем.
            if (hwnd == _hWnd && _source != null) return;

            // Если HWND сменился — отписываемся от старого.
            if (_hWnd != IntPtr.Zero && _hWnd != hwnd)
            {
                Debug.WriteLine("[Hotkey] Attach: HWND изменился, отписываемся от старого.");
                UnregisterAll();
                RemoveHook();
            }

            _hWnd = hwnd;

            var src = HwndSource.FromHwnd(_hWnd);
            if (src == null)
            {
                Debug.WriteLine("[Hotkey] Attach: HwndSource == null.");
                return;
            }

            _source = src;
            _source.AddHook(WndProc);

            // Регистрируем все отложенные.
            foreach (var kv in _registrations)
            {
                if (_registered.Contains(kv.Key)) continue;   // уже зарегистрирован
                if (!_actions.TryGetValue(kv.Key, out var act)) continue;

                TryRegisterOnHwnd(kv.Key, kv.Value.mods, kv.Value.key, isDeferred: true);
            }
        }

        private void DetachFromWindow()
        {
            UnregisterAll();
            RemoveHook();
            _hWnd = IntPtr.Zero;
        }

        private void RemoveHook()
        {
            if (_source != null)
            {
                try { _source.RemoveHook(WndProc); }
                catch (Exception ex) { Debug.WriteLine($"[Hotkey] RemoveHook: {ex}"); }
                _source = null;
            }
        }

        // =============================================================
        // Обработчик Windows-сообщений
        // =============================================================
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
                               ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                Debug.WriteLine($"[Hotkey] WM_HOTKEY id={id}");

                if (_actions.TryGetValue(id, out var action))
                {
                    try { action(); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Hotkey] action error: {ex}");
                    }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        // =============================================================
        // Dispose
        // =============================================================
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Отписки от окна — снимают утечку
            try
            {
                if (_onSourceInitialized != null)
                    _owner.SourceInitialized -= _onSourceInitialized;
                if (_onClosed != null)
                    _owner.Closed -= _onClosed;
            }
            catch { /* окно уже могло быть уничтожено */ }

            UnregisterAll();
            RemoveHook();

            _actions.Clear();
            _registrations.Clear();
            _registered.Clear();

            _onSourceInitialized = null;
            _onClosed = null;
            _hWnd = IntPtr.Zero;
        }
    }
}