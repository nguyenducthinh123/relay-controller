using System;
using System.Windows;
using System.Windows.Interop;
using Relay.ViewModels;

namespace Relay
{
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVICEARRIVAL = 0x8000;

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(HwndMessageHook);
        }

        private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                int eventCode = wParam.ToInt32();
                if (eventCode == DBT_DEVICEREMOVECOMPLETE || eventCode == DBT_DEVICEARRIVAL)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        ViewModel?.HandleHardwareUnplugged();
                    });
                }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            ViewModel?.DisconnectCurrent();
            base.OnClosed(e);
        }
    }
}