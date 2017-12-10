using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LowResPhoto
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private MainViewModel _vm;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _vm?.SaveSetting();            
        }

        private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _vm = DataContext as MainViewModel;
            if(_vm!=null)
                _vm.OnWorkItemCompleted += _vm_OnWorkItemCompleted;
        }

        private void _vm_OnWorkItemCompleted(object sender, WorkItemCompletedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                datagrid.ScrollIntoView(e.Folder);
            }));            
        }
    }
}
