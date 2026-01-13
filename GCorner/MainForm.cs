using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GCorner
{
    public partial class MainForm : Form
    {
        private LowLevelMouseHook _mouseHook;
        private LowLevelKeyboardHook _keyboardHook;
        private bool _isWaitingForInput = false;
        private byte? _originalTaskbarAutoHide = null;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;

        public MainForm()
        {
            InitializeComponent();
            InitializeForm();
            InitializeNotifyIcon();

            SetAutoHideTaskbar(true);
        }

        #region Set ToolWindow
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }
        #endregion

        #region Initialization
        private void InitializeForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Hide();

            _mouseHook = new LowLevelMouseHook();
            _mouseHook.MouseMove += OnMouseMove;
            _mouseHook.MouseClick += OnMouseClick;
            _mouseHook.SetHook();

            _keyboardHook = new LowLevelKeyboardHook();
            _keyboardHook.KeyDown += OnKeyDown;
            _keyboardHook.SetHook();
        }

        private void InitializeNotifyIcon()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Items.Add("退出", null, ExitMenuItem_Click);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = Icon;
            _notifyIcon.ContextMenuStrip = _contextMenu;
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "GCorner";
        }
        #endregion

        #region User Input Events
        private void OnMouseMove(object sender, Point point)
        {
            if (_isWaitingForInput) return;

            if (point.X == 0 && point.Y == 0)
            {
                TriggerTaskView();
            }
        }

        private void OnKeyDown(object sender, int vkCode)
        {
            if (_isWaitingForInput)
            {
                _isWaitingForInput = false;

                SetAutoHideTaskbar(true);
            }
        }

        private void OnMouseClick(object sender, EventArgs e)
        {
            if (_isWaitingForInput)
            {
                _isWaitingForInput = false;

                SetAutoHideTaskbar(true);
            }
        }
        #endregion

        #region WinForm Events
        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _mouseHook?.Unhook();
            _keyboardHook?.Unhook();

            SetAutoHideTaskbar(false);

            _notifyIcon?.Dispose();
            _contextMenu?.Dispose();

            base.OnFormClosing(e);
        }
        #endregion

        #region System Methods
        private void TriggerTaskView()
        {
            keybd_event(0x5B, 0, 0, 0);
            keybd_event(0x09, 0, 0, 0);
            keybd_event(0x09, 0, 2, 0);
            keybd_event(0x5B, 0, 2, 0);

            _isWaitingForInput = true;

            Thread.Sleep(100);
            SetAutoHideTaskbar(false);
        }

        private void SetAutoHideTaskbar(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3", true))
                {
                    if (key != null)
                    {
                        byte[] settings = (byte[])key.GetValue("Settings");
                        if (settings != null && settings.Length > 8)
                        {
                            if (_originalTaskbarAutoHide == null)
                            {
                                _originalTaskbarAutoHide = settings[8];
                            }

                            settings[8] = enable ? (byte)0x02 : (byte)0x03;
                            key.SetValue("Settings", settings);
                        }
                    }
                }

                var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
                abd.lParam = enable ? ABS_AUTOHIDE : ABS_ALWAYSONTOP;
                SHAppBarMessage(ABM_SETSTATE, ref abd);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set auto-hide taskbar: {ex.Message}");
            }
        }
        #endregion

        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        private const uint HWND_BROADCAST = 0xFFFF;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        [DllImport("shell32.dll")]
        private static extern int SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        private const int ABM_SETSTATE = 0x0000000A;
        private const int ABS_AUTOHIDE = 0x0000001;
        private const int ABS_ALWAYSONTOP = 0x0000000;

        #endregion
    }

    #region Hooks
    public class LowLevelMouseHook
    {
        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelMouseProc _proc;

        public event EventHandler<Point> MouseMove;
        public event EventHandler MouseClick;

        public LowLevelMouseHook()
        {
            _proc = HookCallback;
        }

        public void SetHook()
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        public void Unhook()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

                if (wParam == (IntPtr)WM_MOUSEMOVE)
                {
                    MouseMove?.Invoke(this, new Point(hookStruct.pt.x, hookStruct.pt.y));
                }
                else if (wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_RBUTTONDOWN)
                {
                    MouseClick?.Invoke(this, EventArgs.Empty);
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;

        [StructLayout(LayoutKind.Sequential)]
        private class POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private class MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }

    public class LowLevelKeyboardHook
    {
        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;

        public event EventHandler<int> KeyDown;

        public LowLevelKeyboardHook()
        {
            _proc = HookCallback;
        }

        public void SetHook()
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        public void Unhook()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                KeyDown?.Invoke(this, vkCode);
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
    #endregion
}
