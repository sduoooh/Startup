using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Drawing;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Windows.Forms.VisualStyles;

namespace startup
{
    public partial class MainWindow : Window
    {
        private static NotifyIcon ni = new NotifyIcon();
        private Repeater _repeater;
        private bool __visibility = false;

        private const int WM_HOTKEY = 0x0312;
        private const int HOT_KEY_ID = 1;
        private const int HOT_KEY = 0x51; // Q



        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);


        public MainWindow()
        {
            InitializeComponent();
            this._repeater = new Repeater();
            this.DataContext = this._repeater;

            this.SourceInitialized += NotifyIconInitalized;
            this.SourceInitialized += HotKeyRegister;
            this.UserInput.PreviewKeyDown += UserInput_PreviewKeyDown;

        }

        private void NotifyIconInitalized(object o, EventArgs s)
        {
            ni.Icon = new Icon(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon/icon.ico"));
            ni.Text = "hello world!";
            ni.Visible = true;
            ni.Click +=
                delegate (object sender, EventArgs args)
                {
                    this.ActiveStatusChange();
                };
        }

        private enum ASSIST_KEY
        {
            ALT = 0x0001,
            CONTROL = 0x0002,
            SHIFT = 0x0004,
            WIN = 0x0008
        }

        private void HotKeyRegister(object o, EventArgs s)
        {
            IntPtr wptr = new WindowInteropHelper(this).Handle;

            if (wptr == IntPtr.Zero)
            {
                System.Windows.MessageBox.Show("获取窗口句柄失败");
                Environment.Exit(0);
            }
            if (!RegisterHotKey(wptr, HOT_KEY_ID, (uint)ASSIST_KEY.ALT, HOT_KEY))
            {
                System.Windows.MessageBox.Show("注册热键失败");
                Environment.Exit(0);
            }

            HwndSource hs = HwndSource.FromHwnd(wptr);
            hs.AddHook(new HwndSourceHook(HotKeyProcess));

        }
        private IntPtr HotKeyProcess(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOT_KEY_ID)
            {
                this.ActiveStatusChange();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // 激活状态切换
        private void ActiveStatusChange()
        {
            if (this.__visibility) this.Hide();
            else this.Show();

            this._repeater.InputReset();
            this._repeater.InputBoxVisible = Visibility.Collapsed;
            this.__visibility = !this.__visibility;
        }

        // 输入状态切换
        private void ClickEllipse(object sender, MouseButtonEventArgs e)
        {

            if (this._repeater.GetWorkStatus()) return;

            this._repeater.InputReset();
            this._repeater.InputBoxVisible = (Visibility)(2 - this._repeater.InputBoxVisible); // visible: 0, collapsed: 2
        }

        private void UserInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:

                    if (this._repeater.SelectedIndex > -1)
                        this._repeater.SelectedIndex--;
                    else
                        this._repeater.SelectedIndex = this._repeater.WordList.Length - 1;
                    this.UserInput.Select(this.UserInput.Text.Length, 0);
                    e.Handled = true;
                    break;

                case Key.Down:

                    if (this._repeater.SelectedIndex < this._repeater.WordList.Length - 1)
                        this._repeater.SelectedIndex++;
                    else
                        this._repeater.SelectedIndex = -1;
                    this.UserInput.Select(this.UserInput.Text.Length, 0);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    this._repeater.Selected();
                    e.Handled = true;
                    break;
            }
        }

        private void AssociativeListView_MouseUp(object sender,  MouseButtonEventArgs e)
        {
            this.InputBox.Focus();
            this.UserInput.Select(this.UserInput.Text.Length, 0);
        }

        private void AssociativeListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AssociativeListView.SelectedItem != null)
                this._repeater.Selected();
        }


        private void __MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        protected override void OnClosed(EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOT_KEY_ID);
            base.OnClosed(e);
        }
    }
}