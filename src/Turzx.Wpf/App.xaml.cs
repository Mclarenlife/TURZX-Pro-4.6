using System.Windows;

namespace Turzx.Desktop
{
    public partial class App : Application
    {
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            Turzx.Desktop.MainWindow window =
                this.MainWindow as Turzx.Desktop.MainWindow;
            if (window != null) window.PrepareForSystemShutdown();
            base.OnSessionEnding(e);
        }
    }
}
