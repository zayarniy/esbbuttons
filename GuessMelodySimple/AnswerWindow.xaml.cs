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
using System.Windows.Shapes;

namespace GuessMelodySimple
{
    /// <summary>
    /// Interaction logic for AnswerWindow.xaml
    /// </summary>
    public partial class AnswerWindow : Window
    {
        public AnswerWindow(string answerText)
        {
            InitializeComponent();
            TxtAnswer.Text = answerText;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Close();
        }
    }
}
